using System.Diagnostics;
using System.Net.Http;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Simcag.AIService.Application.Configuration;
using Simcag.AIService.Application.Exceptions;
using Simcag.AIService.Application.Interfaces;
using Simcag.AIService.Application.Security;
using Simcag.Shared.Telemetry;

namespace Simcag.AIService.Infrastructure.Clients;

/// <summary>
/// Fila interna de inferência + retentativas + circuit breaker + timeout por tentativa + modelo de fallback.
/// Health/listagem passam direto ao HTTP (sem ocupar workers).
/// </summary>
public sealed class OllamaInferenceCoordinator : IOllamaClient, IHostedService, IAsyncDisposable
{
    private readonly OllamaHttpClient _http;
    private readonly OllamaResilienceOptions _opt;
    private readonly OllamaCircuitBreaker _breaker = new();
    private readonly ILogger<OllamaInferenceCoordinator> _logger;
    private Channel<InferenceWork>? _channel;
    private Task[]? _workers;
    private CancellationTokenSource? _runCts;

    public OllamaInferenceCoordinator(
        OllamaHttpClient http,
        OllamaResilienceOptions opt,
        ILogger<OllamaInferenceCoordinator> logger)
    {
        _http = http;
        _opt = opt;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var runCt = _runCts.Token;
        _channel = Channel.CreateBounded<InferenceWork>(new BoundedChannelOptions(_opt.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });

        _workers = new Task[_opt.MaxConcurrency];
        for (var i = 0; i < _opt.MaxConcurrency; i++)
        {
            var id = i;
            _workers[i] = Task.Run(() => WorkerLoopAsync(id, runCt), runCt);
        }

        _logger.LogInformation(
            "Ollama inference coordinator started (workers={Workers}, queue={Queue}, perAttemptTimeout={PerAttempt}s, retries={Retries}, circuitThreshold={CircuitTh}).",
            _opt.MaxConcurrency,
            _opt.QueueCapacity,
            _opt.PerAttemptTimeoutSeconds,
            _opt.MaxRetries,
            _opt.CircuitFailureThreshold);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
            _channel.Writer.TryComplete();

        if (_workers is { Length: > 0 })
        {
            try
            {
                await Task.WhenAll(_workers).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Some Ollama inference workers did not shut down cleanly.");
            }
        }

        _runCts?.Cancel();
        _runCts?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    public Task<string> GenerateCompletionAsync(string prompt, string model = "llama3.1", CancellationToken ct = default)
    {
        if (_channel is null)
            throw new InvalidOperationException("Ollama inference coordinator not started.");

        return EnqueueAndWaitAsync(prompt, model, useOperationalFallback: true, ct);
    }

    private async Task<string> EnqueueAndWaitAsync(
        string prompt,
        string model,
        bool useOperationalFallback,
        CancellationToken ct)
    {
        var swTotal = Stopwatch.StartNew();
        var queuedAt = Stopwatch.GetTimestamp();
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = new InferenceWork(prompt, model, useOperationalFallback, queuedAt, ct, tcs);
        await _channel!.Writer.WriteAsync(work, ct).ConfigureAwait(false);
        try
        {
            return await tcs.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            SimcagMeters.AiInferenceDurationSeconds.Record(swTotal.Elapsed.TotalSeconds,
                new KeyValuePair<string, object?>("model", model));
        }
    }

    public Task<bool> IsAvailableAsync(CancellationToken ct = default) =>
        _http.IsAvailableAsync(ct);

    public Task<IReadOnlyList<string>> ListInstalledModelNamesAsync(CancellationToken ct = default) =>
        _http.ListInstalledModelNamesAsync(ct);

    private async Task WorkerLoopAsync(int workerId, CancellationToken appStopping)
    {
        var reader = _channel!.Reader;
        while (!appStopping.IsCancellationRequested)
        {
            InferenceWork work;
            try
            {
                if (!await reader.WaitToReadAsync(appStopping).ConfigureAwait(false))
                    break;
                while (reader.TryRead(out work))
                    await ProcessWorkItemAsync(workerId, work, appStopping).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (appStopping.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ollama inference worker {WorkerId} loop error.", workerId);
            }
        }
    }

    private async Task ProcessWorkItemAsync(int workerId, InferenceWork work, CancellationToken appStopping)
    {
        var waitSeconds = (Stopwatch.GetTimestamp() - work.QueuedTimestamp) / (double)Stopwatch.Frequency;
        if (waitSeconds > 0.000_1)
        {
            SimcagMeters.AiInferenceQueueWaitSeconds.Record(waitSeconds,
                new KeyValuePair<string, object?>("model", work.Model));
        }

        if (work.CallerCancellation.IsCancellationRequested)
        {
            work.Tcs.TrySetCanceled(work.CallerCancellation);
            return;
        }

        var utcNow = DateTime.UtcNow;
        if (_breaker.IsOpen(utcNow))
        {
            SimcagMeters.AiInferenceCircuitOpen.Add(1,
                new KeyValuePair<string, object?>("model", work.Model));
            work.Tcs.TrySetException(new AiServiceException("AI inference circuit is open; try again later."));
            return;
        }

        try
        {
            var text = await ExecuteWithResilienceAsync(work.Prompt, work.Model, work.UseOperationalFallback, work.CallerCancellation, appStopping)
                .ConfigureAwait(false);
            _breaker.RecordSuccess();
            work.Tcs.TrySetResult(text);
        }
        catch (OperationCanceledException) when (work.CallerCancellation.IsCancellationRequested)
        {
            work.Tcs.TrySetCanceled(work.CallerCancellation);
        }
        catch (Exception ex)
        {
            _breaker.RecordFailure(_opt.CircuitFailureThreshold, _opt.CircuitOpenSeconds, DateTime.UtcNow);
            work.Tcs.TrySetException(ex);
        }
    }

    private async Task<string> ExecuteWithResilienceAsync(
        string prompt,
        string primaryModel,
        bool useOperationalFallback,
        CancellationToken callerCt,
        CancellationToken appStopping)
    {
        if (!LlmPromptSafety.TryEvaluate(prompt, LlmPromptSafety.ShouldBlock, _logger, out _))
            throw new AiServiceException("Prompt rejected by safety policy.");

        Exception? last = null;
        foreach (var model in BuildModelSequence(primaryModel, useOperationalFallback))
        {
            for (var attempt = 0; attempt <= _opt.MaxRetries; attempt++)
            {
                callerCt.ThrowIfCancellationRequested();
                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(callerCt, appStopping);
                attemptCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_opt.PerAttemptTimeoutSeconds, 5, 600)));
                var attemptCt = attemptCts.Token;
                try
                {
                    var outcome = await _http.GenerateCompletionRawAsync(prompt, model, attemptCt).ConfigureAwait(false);
                    if (outcome.PromptEvalCount is { } pc)
                        SimcagMeters.AiInferenceTokensReported.Add(pc, new KeyValuePair<string, object?>("kind", "prompt_eval"));
                    if (outcome.EvalCount is { } ec)
                        SimcagMeters.AiInferenceTokensReported.Add(ec, new KeyValuePair<string, object?>("kind", "eval"));
                    if (!string.Equals(model, primaryModel, StringComparison.OrdinalIgnoreCase))
                    {
                        SimcagMeters.AiInferenceFallbackModelUsed.Add(1,
                            new KeyValuePair<string, object?>("primary", primaryModel),
                            new KeyValuePair<string, object?>("used", model));
                    }

                    return outcome.Text;
                }
                catch (Exception ex) when (IsTransient(ex))
                {
                    last = ex;
                    if (ex is TaskCanceledException && !callerCt.IsCancellationRequested)
                        SimcagMeters.AiInferenceTimeouts.Add(1);
                    if (attempt < _opt.MaxRetries)
                    {
                        SimcagMeters.AiInferenceRetries.Add(1,
                            new KeyValuePair<string, object?>("reason", ClassifyRetry(ex)),
                            new KeyValuePair<string, object?>("model", model));
                        var delayMs = (int)(_opt.RetryBaseDelayMilliseconds * Math.Pow(2, attempt));
                        await Task.Delay(Math.Clamp(delayMs, 50, 30_000), callerCt).ConfigureAwait(false);
                        continue;
                    }

                    break;
                }
            }
        }

        SimcagMeters.AiInferenceFailures.Add(1, new KeyValuePair<string, object?>("reason", "exhausted"));
        throw last ?? new AiServiceException("AI inference failed after retries.");
    }

    private IEnumerable<string> BuildModelSequence(string primary, bool useOperationalFallback)
    {
        yield return primary;
        if (!useOperationalFallback)
            yield break;
        var fb = _opt.OperationalFallbackModel;
        if (string.IsNullOrWhiteSpace(fb))
            yield break;
        if (string.Equals(fb.Trim(), primary.Trim(), StringComparison.OrdinalIgnoreCase))
            yield break;
        yield return fb.Trim();
    }

    private static string ClassifyRetry(Exception ex) =>
        ex switch
        {
            TaskCanceledException => "timeout",
            OperationCanceledException => "canceled",
            HttpRequestException h when h.StatusCode is { } s => $"http_{(int)s}",
            AiServiceException { InnerException: HttpRequestException hh } when hh.StatusCode is { } s2 => $"http_{(int)s2}",
            AiServiceException { InnerException: TaskCanceledException } => "timeout",
            _ => "transient"
        };

    private static bool IsTransient(Exception ex)
    {
        if (ex is TaskCanceledException or OperationCanceledException)
            return true;
        if (ex is HttpRequestException h)
        {
            if (!h.StatusCode.HasValue)
                return true;
            var c = (int)h.StatusCode.Value;
            return c >= 500 || c == 408 || c == 429;
        }

        if (ex is AiServiceException ai)
            return IsTransient(ai.InnerException ?? ai);

        return false;
    }

    private readonly record struct InferenceWork(
        string Prompt,
        string Model,
        bool UseOperationalFallback,
        long QueuedTimestamp,
        CancellationToken CallerCancellation,
        TaskCompletionSource<string> Tcs);
}
