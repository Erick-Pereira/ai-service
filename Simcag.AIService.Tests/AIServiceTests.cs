using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Simcag.AIService.Application.Interfaces;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Simcag.AIService.Tests;

public class AIServiceTests
{
    private readonly Mock<IOllamaClient> _ollamaClientMock;
    private readonly Mock<ILogger<Simcag.AIService.Application.Services.AIService>> _loggerMock;
    private readonly Mock<ICategoryRepository> _categoryRepositoryMock;
    private readonly Mock<IAIProcessingResultRepository> _processingResultRepositoryMock;
    private readonly Mock<Simcag.Shared.Messaging.Contracts.IEventPublisher<Simcag.Shared.Events.NormalizedDataEvent>> _normalizedPublisherMock;
    private readonly Mock<Simcag.Shared.Messaging.Contracts.IEventPublisher<Simcag.Shared.Events.CategorizedDataEvent>> _categorizedPublisherMock;
    private readonly Simcag.AIService.Application.Services.AIService _aiService;

    public AIServiceTests()
    {
        _ollamaClientMock = new Mock<IOllamaClient>();
        _loggerMock = new Mock<ILogger<Simcag.AIService.Application.Services.AIService>>();
        _categoryRepositoryMock = new Mock<ICategoryRepository>();
        _processingResultRepositoryMock = new Mock<IAIProcessingResultRepository>();
        _normalizedPublisherMock = new Mock<Simcag.Shared.Messaging.Contracts.IEventPublisher<Simcag.Shared.Events.NormalizedDataEvent>>();
        _categorizedPublisherMock = new Mock<Simcag.Shared.Messaging.Contracts.IEventPublisher<Simcag.Shared.Events.CategorizedDataEvent>>();

        _aiService = new Simcag.AIService.Application.Services.AIService(
            _ollamaClientMock.Object,
            _loggerMock.Object,
            _categoryRepositoryMock.Object,
            _processingResultRepositoryMock.Object,
            _normalizedPublisherMock.Object,
            _categorizedPublisherMock.Object);
    }

    [Fact]
    public async Task CategorizeProductAsync_ShouldReturnAICategory_WhenOllamaIsAvailable()
    {
        var productDescription = "Dell Inspiron 15 Notebook i7";
        var expectedCategory = Simcag.AIService.Domain.Entities.ProductCategory.Create("Notebook", "Laptops and notebooks");

        _ollamaClientMock.Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _ollamaClientMock.Setup(x => x.GenerateCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Notebook");
        _categoryRepositoryMock.Setup(x => x.GetByNameAsync("Notebook", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedCategory);

        var result = await _aiService.CategorizeProductAsync(productDescription, CancellationToken.None);

        result.Should().NotBeNull();
        result.CategoryName.Should().Be("Notebook");
        result.UsedFallback.Should().BeFalse();
        result.Confidence.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CategorizeProductAsync_ShouldUseFallback_WhenOllamaIsUnavailable()
    {
        var productDescription = "Dell Inspiron 15 Notebook i7";

        _ollamaClientMock.Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _aiService.CategorizeProductAsync(productDescription, CancellationToken.None);

        result.Should().NotBeNull();
        result.UsedFallback.Should().BeTrue();
    }

    [Fact]
    public async Task ExtractEntitiesAsync_ShouldReturnExtractedData_WhenOllamaIsAvailable()
    {
        var productDescription = "Dell Inspiron 15 i7 16GB RAM";
        var mockResponse = "{\"brand\": \"Dell\", \"model\": \"Inspiron 15\", \"features\": [\"i7\", \"16GB RAM\"]}";

        _ollamaClientMock.Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _ollamaClientMock.Setup(x => x.GenerateCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        var result = await _aiService.ExtractEntitiesAsync(productDescription, CancellationToken.None);

        result.Should().NotBeNull();
        result.Brand.Should().Be("Dell");
        result.UsedFallback.Should().BeFalse();
    }

    [Fact]
    public async Task StandardizeNameAsync_ShouldReturnStandardizedName_WhenOllamaIsAvailable()
    {
        var productName = "DELL INSPIRON 15 NOTEBOOK";
        var standardizedName = "Dell Inspiron 15 Notebook";

        _ollamaClientMock.Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _ollamaClientMock.Setup(x => x.GenerateCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(standardizedName);

        var result = await _aiService.StandardizeNameAsync(productName, CancellationToken.None);

        result.Should().NotBeNull();
        result.StandardizedName.Should().Be(standardizedName);
        result.UsedFallback.Should().BeFalse();
    }

    [Fact]
    public async Task StandardizeNameAsync_ShouldUseFallback_WhenOllamaIsUnavailable()
    {
        var productName = "dell inspiron 15  notebook";

        _ollamaClientMock.Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _aiService.StandardizeNameAsync(productName, CancellationToken.None);

        result.Should().NotBeNull();
        result.StandardizedName.Should().Be("Dell Inspiron 15 Notebook");
        result.UsedFallback.Should().BeTrue();
    }
}