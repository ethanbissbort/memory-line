using FluentAssertions;
using MemoryTimeline.Core.Services;
using Xunit;

namespace MemoryTimeline.Tests.UnitTests;

/// <summary>
/// Unit tests for the pure pooling/normalization statics of
/// <see cref="OnnxEmbeddingService"/> — exercised without an inference session.
/// </summary>
public class OnnxEmbeddingMathTests
{
    #region MeanPool

    [Fact]
    public void MeanPool_AllTokensAttended_AveragesHiddenStates()
    {
        // Arrange - 3 tokens, hidden size 2, flattened row-major
        var hiddenStates = new float[] { 1f, 2f, 3f, 4f, 5f, 6f };
        var mask = new long[] { 1, 1, 1 };

        // Act
        var pooled = OnnxEmbeddingService.MeanPool(hiddenStates, mask, sequenceLength: 3, hiddenSize: 2);

        // Assert
        pooled.Should().Equal(3f, 4f);
    }

    [Fact]
    public void MeanPool_MaskedTokens_AreExcludedFromTheAverage()
    {
        // Arrange - the third token is masked out; its huge values must not
        // leak into the pooled vector
        var hiddenStates = new float[] { 1f, 2f, 3f, 4f, 100f, 200f };
        var mask = new long[] { 1, 1, 0 };

        // Act
        var pooled = OnnxEmbeddingService.MeanPool(hiddenStates, mask, sequenceLength: 3, hiddenSize: 2);

        // Assert
        pooled.Should().Equal(2f, 3f);
    }

    [Fact]
    public void MeanPool_AllMasked_ReturnsZeroVector()
    {
        // Arrange
        var hiddenStates = new float[] { 1f, 2f, 3f, 4f };
        var mask = new long[] { 0, 0 };

        // Act
        var pooled = OnnxEmbeddingService.MeanPool(hiddenStates, mask, sequenceLength: 2, hiddenSize: 2);

        // Assert
        pooled.Should().Equal(0f, 0f);
    }

    [Fact]
    public void MeanPool_ShapeSmallerThanDeclared_Throws()
    {
        // Arrange
        var hiddenStates = new float[] { 1f, 2f };
        var mask = new long[] { 1, 1 };

        // Act
        var act = () => OnnxEmbeddingService.MeanPool(hiddenStates, mask, sequenceLength: 2, hiddenSize: 2);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region L2NormalizeInPlace

    [Fact]
    public void L2Normalize_ProducesUnitVector()
    {
        // Arrange - classic 3-4-5 triangle
        var vector = new float[] { 3f, 4f };

        // Act
        OnnxEmbeddingService.L2NormalizeInPlace(vector);

        // Assert
        vector[0].Should().BeApproximately(0.6f, 1e-6f);
        vector[1].Should().BeApproximately(0.8f, 1e-6f);

        var norm = Math.Sqrt(vector.Sum(v => (double)v * v));
        norm.Should().BeApproximately(1.0, 1e-6);
    }

    [Fact]
    public void L2Normalize_AlreadyUnitVector_IsUnchanged()
    {
        // Arrange
        var vector = new float[] { 1f, 0f, 0f };

        // Act
        OnnxEmbeddingService.L2NormalizeInPlace(vector);

        // Assert
        vector.Should().Equal(1f, 0f, 0f);
    }

    [Fact]
    public void L2Normalize_ZeroVector_LeftAsIs()
    {
        // Arrange
        var vector = new float[] { 0f, 0f, 0f };

        // Act
        OnnxEmbeddingService.L2NormalizeInPlace(vector);

        // Assert - no NaN from dividing by a zero norm
        vector.Should().Equal(0f, 0f, 0f);
    }

    [Fact]
    public void MeanPool_ThenNormalize_YieldsUnitVectorForRealisticShape()
    {
        // Arrange - 4 tokens, hidden size 3, arbitrary values
        var hiddenStates = new float[]
        {
            0.5f, -1.0f, 2.0f,
            1.5f,  0.5f, -0.5f,
            -2.0f, 1.0f, 0.0f,
            0.0f,  0.0f, 1.0f
        };
        var mask = new long[] { 1, 1, 1, 0 };

        // Act
        var pooled = OnnxEmbeddingService.MeanPool(hiddenStates, mask, sequenceLength: 4, hiddenSize: 3);
        OnnxEmbeddingService.L2NormalizeInPlace(pooled);

        // Assert
        var norm = Math.Sqrt(pooled.Sum(v => (double)v * v));
        norm.Should().BeApproximately(1.0, 1e-6);
    }

    #endregion
}
