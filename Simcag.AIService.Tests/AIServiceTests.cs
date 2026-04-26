using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Simcag.AIService.Application.Contracts;
using Simcag.AIService.Application.Interfaces;
using Simcag.AIService.Application.Services;
using Simcag.AIService.Domain.Entities;
using Simcag.AIService.Domain.Services;
using Simcag.AIService.Domain.ValueObjects;
using Simcag.Shared.Events;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Simcag.AIService.Tests;

/// <summary>
/// Tests for Financial AI Services after refactoring.
/// </summary>
public class FinancialAIServiceTests
{
    // ---------- Expense Classification Tests ----------
    [Fact]
    public async Task ClassifyAsync_ShouldReturnAICategory_WhenOllamaAvailable()
    {
        // Arrange
        var ollamaMock = new Mock<IOllamaClient>();
        var loggerMock = new Mock<ILogger<ExpenseClassificationService>>();
        var repoMock = new Mock<ICategoryRepository>();
        var matcherMock = new Mock<ICategoryMatcher>();
        var extractorMock = new Mock<ICategoryResponseExtractor>();
        var confidenceMock = new Mock<IConfidenceCalculator>();

        var rawData = new RawFinancialDataEvent
        {
            DocumentId = "doc123",
            RawText = "Dell Inspiron 15 Notebook i7",
            DocumentType = "Invoice",
            Source = "test",
            FileHash = string.Empty,
            ExtractedFields = new Dictionary<string, object?>(),
            OccurredAt = DateTime.UtcNow,
            Timestamp = DateTime.UtcNow,
            ExtractedItems = null
        };

        var categoryEntity = ProductCategory.Create("Notebook", "Laptops");
        var aiResponse = "Notebook";
        var extractedCategory = new CategoryName("Notebook");
        var confidenceScore = new ConfidenceScore(0.9m);

        ollamaMock.Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        ollamaMock.Setup(x => x.GenerateCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);
        repoMock.Setup(x => x.GetByNameAsync("Notebook", It.IsAny<CancellationToken>())).ReturnsAsync(categoryEntity);
        extractorMock.Setup(x => x.Extract(aiResponse)).Returns(extractedCategory);
        confidenceMock.Setup(x => x.Calculate(aiResponse, extractedCategory)).Returns(confidenceScore);

        var inferenceCacheMock = new Mock<IAiInferenceCache>();
        var service = new ExpenseClassificationService(
            ollamaMock.Object, loggerMock.Object, repoMock.Object,
            matcherMock.Object, extractorMock.Object, confidenceMock.Object, inferenceCacheMock.Object);

        // Act
        var result = await service.ClassifyAsync(rawData, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.CategoryName.Should().Be("Notebook");
        result.UsedFallback.Should().BeFalse();
        result.Confidence.Should().Be(0.9m);
    }

    [Fact]
    public async Task ClassifyAsync_ShouldUseFallback_WhenOllamaUnavailable()
    {
        // Arrange
        var ollamaMock = new Mock<IOllamaClient>();
        var loggerMock = new Mock<ILogger<ExpenseClassificationService>>();
        var repoMock = new Mock<ICategoryRepository>();
        var matcher = new CategoryMatcher();
        var extractorMock = new Mock<ICategoryResponseExtractor>();
        var confidenceMock = new Mock<IConfidenceCalculator>();

        var rawData = new RawFinancialDataEvent
        {
            DocumentId = "doc123",
            RawText = "CPU Intel i7",
            DocumentType = "Invoice",
            Source = "test",
            OccurredAt = DateTime.UtcNow,
            Timestamp = DateTime.UtcNow,
            ExtractedFields = new Dictionary<string, object?>(),
            FileHash = string.Empty,
            ExtractedItems = null
        };

        ollamaMock.Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var hardwareCategory = ProductCategory.Create("Hardware", "Hardware components");
        repoMock.Setup(x => x.GetByNameAsync("Hardware", It.IsAny<CancellationToken>())).ReturnsAsync(hardwareCategory);
        repoMock.Setup(x => x.GetByNameAsync("Outro", It.IsAny<CancellationToken>())).ReturnsAsync(ProductCategory.Create("Outro", "Other"));

        var inferenceCacheMock = new Mock<IAiInferenceCache>();
        var service = new ExpenseClassificationService(
            ollamaMock.Object, loggerMock.Object, repoMock.Object,
            matcher, extractorMock.Object, confidenceMock.Object, inferenceCacheMock.Object);

        // Act
        var result = await service.ClassifyAsync(rawData, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.UsedFallback.Should().BeTrue();
        result.CategoryName.Should().Be("Hardware");
    }

    // ---------- Supplier Extraction Tests ----------
    [Fact]
    public async Task ExtractAsync_ShouldReturnExtractedData_WhenOllamaAvailable()
    {
        // Arrange
        var ollamaMock = new Mock<IOllamaClient>();
        var loggerMock = new Mock<ILogger<SupplierExtractionService>>();

        var rawData = new RawFinancialDataEvent
        {
            DocumentId = "doc123",
            RawText = "Supplier: Dell, CNPJ: 12.345.678/0001-00",
            DocumentType = "Invoice",
            Source = "test",
            OccurredAt = DateTime.UtcNow,
            Timestamp = DateTime.UtcNow,
            ExtractedFields = new Dictionary<string, object?>(),
            FileHash = string.Empty,
            ExtractedItems = null
        };

        var mockResponse = "{\"supplierName\": \"Dell\", \"taxId\": \"12.345.678/0001-00\"}";
        ollamaMock.Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        ollamaMock.Setup(x => x.GenerateCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        var nameNormalizationMock = new Mock<INameNormalizationService>();
        var inferenceCacheMock = new Mock<IAiInferenceCache>();
        var service = new SupplierExtractionService(ollamaMock.Object, loggerMock.Object, nameNormalizationMock.Object, inferenceCacheMock.Object);

        // Act
        var result = await service.ExtractAsync(rawData, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.RawSupplierName.Should().Be("Dell");
        result.TaxId.Should().Be("12.345.678/0001-00");
        result.UsedFallback.Should().BeFalse();
        result.Product.Should().NotBeNull();
        result.Product.UsedFallback.Should().BeTrue();
        result.Product.Features.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractAsync_ShouldParseBrandModelFeatures_WhenPresentInJson()
    {
        var ollamaMock = new Mock<IOllamaClient>();
        var loggerMock = new Mock<ILogger<SupplierExtractionService>>();

        var rawData = new RawFinancialDataEvent
        {
            DocumentId = "doc999",
            RawText = "NF Dell",
            DocumentType = "Invoice",
            Source = "test",
            OccurredAt = DateTime.UtcNow,
            Timestamp = DateTime.UtcNow,
            ExtractedFields = new Dictionary<string, object?>(),
            FileHash = string.Empty,
            ExtractedItems = null
        };

        var mockResponse =
            "{\"supplierName\":\"Dell\",\"taxId\":null,\"brand\":\"Dell\",\"model\":\"Inspiron 15\",\"features\":[\"16GB RAM\",\"512GB SSD\"],\"supplierConfidence\":0.9,\"supplierUsedFallback\":false,\"productConfidence\":0.88,\"productUsedFallback\":false}";
        ollamaMock.Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        ollamaMock.Setup(x => x.GenerateCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        var nameNormalizationMock = new Mock<INameNormalizationService>();
        nameNormalizationMock
            .Setup(x => x.NormalizeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NormalizedNameResult("Dell", "DELL", 0.8m, false));

        var inferenceCacheMock = new Mock<IAiInferenceCache>();
        var service = new SupplierExtractionService(ollamaMock.Object, loggerMock.Object, nameNormalizationMock.Object, inferenceCacheMock.Object);

        var result = await service.ExtractAsync(rawData, CancellationToken.None);

        result.Product.Brand.Should().Be("Dell");
        result.Product.Model.Should().Be("Inspiron 15");
        result.Product.Features.Should().ContainInOrder("16GB RAM", "512GB SSD");
        result.Product.Confidence.Should().Be(0.88m);
        result.Product.UsedFallback.Should().BeFalse();
        result.Confidence.Should().Be(0.9m);
        result.UsedFallback.Should().BeFalse();
    }

    [Fact]
    public async Task ExtractAsync_ShouldUseFallback_WhenOllamaUnavailable()
    {
        // Arrange
        var ollamaMock = new Mock<IOllamaClient>();
        var loggerMock = new Mock<ILogger<SupplierExtractionService>>();

        var rawData = new RawFinancialDataEvent
        {
            DocumentId = "doc123",
            RawText = "DELL COMPUTERS LTDA - CNPJ: 12.345.678/0001-00",
            DocumentType = "Invoice",
            Source = "test",
            OccurredAt = DateTime.UtcNow,
            Timestamp = DateTime.UtcNow,
            ExtractedFields = new Dictionary<string, object?>(),
            FileHash = string.Empty,
            ExtractedItems = null
        };

        ollamaMock.Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var nameNormalizationMock = new Mock<INameNormalizationService>();
        var inferenceCacheMock = new Mock<IAiInferenceCache>();
        var service = new SupplierExtractionService(ollamaMock.Object, loggerMock.Object, nameNormalizationMock.Object, inferenceCacheMock.Object);

        // Act
        var result = await service.ExtractAsync(rawData, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.UsedFallback.Should().BeTrue();
        result.RawSupplierName.Should().NotBeNullOrEmpty();
    }
}