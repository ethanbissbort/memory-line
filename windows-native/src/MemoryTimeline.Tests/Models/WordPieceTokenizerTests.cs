using FluentAssertions;
using MemoryTimeline.Core.Models;
using Xunit;

namespace MemoryTimeline.Tests.Models;

/// <summary>
/// Unit tests for the pure BERT WordPiece tokenizer used by the local ONNX
/// embedding service, driven by a small hand-built vocabulary.
/// </summary>
public class WordPieceTokenizerTests
{
    /// <summary>
    /// Builds a tokenizer over the special tokens plus the supplied pieces.
    /// Ids are assigned by position: [PAD]=0, [UNK]=1, [CLS]=2, [SEP]=3, then
    /// the extra pieces in order starting at 4.
    /// </summary>
    private static WordPieceTokenizer CreateTokenizer(params string[] extraPieces)
    {
        var vocabulary = BuildVocabulary(extraPieces);
        return new WordPieceTokenizer(vocabulary);
    }

    private static Dictionary<string, int> BuildVocabulary(params string[] extraPieces)
    {
        var tokens = new List<string> { "[PAD]", "[UNK]", "[CLS]", "[SEP]" };
        tokens.AddRange(extraPieces);
        return tokens.Select((token, index) => (token, index))
            .ToDictionary(x => x.token, x => x.index);
    }

    private static long Id(WordPieceTokenizer _, string[] extraPieces, string piece)
    {
        var vocabulary = BuildVocabulary(extraPieces);
        return vocabulary[piece];
    }

    #region Special tokens / basic shape

    [Fact]
    public void Encode_SimpleWord_WrapsInClsAndSep()
    {
        // Arrange
        var pieces = new[] { "hello" };
        var tokenizer = CreateTokenizer(pieces);

        // Act
        var ids = tokenizer.Encode("hello");

        // Assert
        ids.Should().Equal(tokenizer.ClsTokenId, Id(tokenizer, pieces, "hello"), tokenizer.SepTokenId);
    }

    [Fact]
    public void Encode_EmptyText_ProducesOnlyClsSep()
    {
        // Arrange
        var tokenizer = CreateTokenizer();

        // Act
        var ids = tokenizer.Encode("   ");

        // Assert
        ids.Should().Equal(tokenizer.ClsTokenId, tokenizer.SepTokenId);
    }

    [Fact]
    public void Constructor_MissingSpecialTokens_Throws()
    {
        // Arrange - vocabulary without [CLS]/[SEP]/[UNK]
        var vocabulary = new Dictionary<string, int> { ["hello"] = 0 };

        // Act
        var act = () => new WordPieceTokenizer(vocabulary);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*[CLS]*");
    }

    #endregion

    #region Greedy longest match + continuations

    [Fact]
    public void Encode_GreedyMatch_PrefersLongestPiece()
    {
        // Arrange - both "play" and "playing" are in the vocab: the greedy
        // longest-match must take "playing" whole, not "play" + "##ing".
        var pieces = new[] { "play", "playing", "##ing" };
        var tokenizer = CreateTokenizer(pieces);

        // Act
        var ids = tokenizer.Encode("playing");

        // Assert
        ids.Should().Equal(tokenizer.ClsTokenId, Id(tokenizer, pieces, "playing"), tokenizer.SepTokenId);
    }

    [Fact]
    public void Encode_ContinuationPieces_UseHashHashPrefix()
    {
        // Arrange - "playing" is absent, so the word must split into
        // "play" + "##ing" (continuation pieces carry the ## prefix).
        var pieces = new[] { "play", "##ing" };
        var tokenizer = CreateTokenizer(pieces);

        // Act
        var ids = tokenizer.Encode("playing");

        // Assert
        ids.Should().Equal(
            tokenizer.ClsTokenId,
            Id(tokenizer, pieces, "play"),
            Id(tokenizer, pieces, "##ing"),
            tokenizer.SepTokenId);
    }

    [Fact]
    public void Encode_MultiPieceContinuationChain_MatchesGreedily()
    {
        // Arrange
        var pieces = new[] { "un", "##aff", "##able" };
        var tokenizer = CreateTokenizer(pieces);

        // Act
        var ids = tokenizer.Encode("unaffable");

        // Assert
        ids.Should().Equal(
            tokenizer.ClsTokenId,
            Id(tokenizer, pieces, "un"),
            Id(tokenizer, pieces, "##aff"),
            Id(tokenizer, pieces, "##able"),
            tokenizer.SepTokenId);
    }

    #endregion

    #region UNK fallback

    [Fact]
    public void Encode_UnknownWord_MapsToUnk()
    {
        // Arrange
        var tokenizer = CreateTokenizer("hello");

        // Act
        var ids = tokenizer.Encode("xyzzy");

        // Assert
        ids.Should().Equal(tokenizer.ClsTokenId, tokenizer.UnkTokenId, tokenizer.SepTokenId);
    }

    [Fact]
    public void Encode_PartiallyMatchableWord_CollapsesWholeWordToUnk()
    {
        // Arrange - "play" matches but the "zzz" remainder has no ## piece:
        // BERT semantics collapse the ENTIRE word to [UNK], not play+[UNK].
        var tokenizer = CreateTokenizer("play");

        // Act
        var ids = tokenizer.Encode("playzzz");

        // Assert
        ids.Should().Equal(tokenizer.ClsTokenId, tokenizer.UnkTokenId, tokenizer.SepTokenId);
    }

    #endregion

    #region Normalization (case, accents, punctuation, whitespace)

    [Fact]
    public void Encode_UppercaseAndAccents_NormalizeToLowercaseStripped()
    {
        // Arrange - "Héllo" must normalize to "hello" (lowercase + FormD
        // decomposition with NonSpacingMark combining chars removed).
        var pieces = new[] { "hello" };
        var tokenizer = CreateTokenizer(pieces);

        // Act
        var ids = tokenizer.Encode("Héllo");

        // Assert
        ids.Should().Equal(tokenizer.ClsTokenId, Id(tokenizer, pieces, "hello"), tokenizer.SepTokenId);
    }

    [Fact]
    public void Encode_Punctuation_BecomesOwnToken()
    {
        // Arrange
        var pieces = new[] { "hello", "world", ",", "!" };
        var tokenizer = CreateTokenizer(pieces);

        // Act - punctuation splits words even without surrounding whitespace
        var ids = tokenizer.Encode("hello,world!");

        // Assert
        ids.Should().Equal(
            tokenizer.ClsTokenId,
            Id(tokenizer, pieces, "hello"),
            Id(tokenizer, pieces, ","),
            Id(tokenizer, pieces, "world"),
            Id(tokenizer, pieces, "!"),
            tokenizer.SepTokenId);
    }

    [Fact]
    public void NormalizeAndSplit_MixedInput_SplitsOnWhitespaceAndPunctuation()
    {
        // Act
        var tokens = WordPieceTokenizer.NormalizeAndSplit("Café au lait, s'il vous plaît!");

        // Assert
        tokens.Should().Equal("cafe", "au", "lait", ",", "s", "'", "il", "vous", "plait", "!");
    }

    [Fact]
    public void NormalizeAndSplit_EmptyOrWhitespace_ReturnsNoTokens()
    {
        WordPieceTokenizer.NormalizeAndSplit("").Should().BeEmpty();
        WordPieceTokenizer.NormalizeAndSplit(" \t\n ").Should().BeEmpty();
    }

    #endregion

    #region Truncation

    [Fact]
    public void Encode_LongInput_TruncatesTo256TokensKeepingSep()
    {
        // Arrange - 300 known words would exceed the 256-token cap
        var pieces = new[] { "a" };
        var tokenizer = CreateTokenizer(pieces);
        var text = string.Join(' ', Enumerable.Repeat("a", 300));

        // Act
        var ids = tokenizer.Encode(text);

        // Assert - exactly MaxSequenceLength tokens: [CLS] + 254 pieces + [SEP]
        ids.Should().HaveCount(WordPieceTokenizer.MaxSequenceLength);
        ids[0].Should().Be(tokenizer.ClsTokenId);
        ids[^1].Should().Be(tokenizer.SepTokenId);
        ids.Skip(1).Take(WordPieceTokenizer.MaxSequenceLength - 2)
            .Should().OnlyContain(id => id == Id(tokenizer, pieces, "a"));
    }

    [Fact]
    public void Encode_InputBelowLimit_IsNotTruncated()
    {
        // Arrange
        var pieces = new[] { "a" };
        var tokenizer = CreateTokenizer(pieces);
        var text = string.Join(' ', Enumerable.Repeat("a", 10));

        // Act
        var ids = tokenizer.Encode(text);

        // Assert
        ids.Should().HaveCount(12); // [CLS] + 10 + [SEP]
    }

    #endregion
}
