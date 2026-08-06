using System.Globalization;
using System.Text;

namespace MemoryTimeline.Core.Models;

/// <summary>
/// Minimal BERT WordPiece tokenizer for the local embedding model
/// (all-MiniLM-L6-v2 uses the uncased BERT vocabulary). Pure and dependency
/// free so it is fully unit-testable with a small hand-built vocabulary.
/// <para>
/// Pipeline (matching BERT's uncased BasicTokenizer + WordpieceTokenizer):
/// lowercase, strip accents (Unicode FormD, drop NonSpacingMark), split on
/// whitespace with each punctuation character as its own token, then greedy
/// longest-match WordPiece with "##" continuation pieces and an [UNK] fallback
/// for any word that cannot be fully pieced. Sequences are wrapped in
/// [CLS] ... [SEP] and truncated to <see cref="MaxSequenceLength"/> tokens.
/// </para>
/// </summary>
public sealed class WordPieceTokenizer
{
    public const string ClsToken = "[CLS]";
    public const string SepToken = "[SEP]";
    public const string UnkToken = "[UNK]";
    public const string ContinuationPrefix = "##";

    /// <summary>
    /// Maximum encoded sequence length INCLUDING [CLS] and [SEP]. 256 keeps
    /// inference fast; event titles + descriptions rarely come close.
    /// </summary>
    public const int MaxSequenceLength = 256;

    private readonly IReadOnlyDictionary<string, int> _vocabulary;

    public int ClsTokenId { get; }
    public int SepTokenId { get; }
    public int UnkTokenId { get; }

    /// <param name="vocabulary">
    /// Token → id map (one entry per vocab.txt line). Must contain the [CLS],
    /// [SEP] and [UNK] special tokens.
    /// </param>
    public WordPieceTokenizer(IReadOnlyDictionary<string, int> vocabulary)
    {
        _vocabulary = vocabulary ?? throw new ArgumentNullException(nameof(vocabulary));

        if (!vocabulary.TryGetValue(ClsToken, out var clsId) ||
            !vocabulary.TryGetValue(SepToken, out var sepId) ||
            !vocabulary.TryGetValue(UnkToken, out var unkId))
        {
            throw new ArgumentException(
                $"Vocabulary must contain the {ClsToken}, {SepToken} and {UnkToken} special tokens.",
                nameof(vocabulary));
        }

        ClsTokenId = clsId;
        SepTokenId = sepId;
        UnkTokenId = unkId;
    }

    /// <summary>
    /// Encodes text into token ids: [CLS] + word pieces + [SEP], truncated to
    /// <see cref="MaxSequenceLength"/> tokens (the [SEP] terminator is always kept).
    /// </summary>
    public IReadOnlyList<long> Encode(string text)
    {
        var ids = new List<long> { ClsTokenId };

        foreach (var word in NormalizeAndSplit(text))
        {
            if (ids.Count >= MaxSequenceLength - 1)
            {
                break; // reserve the final slot for [SEP]
            }

            foreach (var pieceId in TokenizeWord(word))
            {
                if (ids.Count >= MaxSequenceLength - 1)
                {
                    break;
                }
                ids.Add(pieceId);
            }
        }

        ids.Add(SepTokenId);
        return ids;
    }

    /// <summary>
    /// BERT-style basic normalization: lowercase, strip accents (decompose to
    /// FormD and drop NonSpacingMark combining characters), then split on
    /// whitespace with every punctuation/symbol character emitted as its own
    /// token ("hello,world" → ["hello", ",", "world"]).
    /// </summary>
    public static IReadOnlyList<string> NormalizeAndSplit(string text)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return tokens;
        }

        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);

        var current = new StringBuilder();
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue; // accent stripped
            }

            if (char.IsWhiteSpace(ch))
            {
                FlushCurrent(tokens, current);
            }
            else if (IsPunctuation(ch))
            {
                FlushCurrent(tokens, current);
                tokens.Add(ch.ToString());
            }
            else
            {
                current.Append(ch);
            }
        }

        FlushCurrent(tokens, current);
        return tokens;
    }

    /// <summary>
    /// Greedy longest-match WordPiece for a single normalized word. Pieces
    /// after the first carry the "##" continuation prefix. If ANY position has
    /// no vocabulary match, the whole word collapses to [UNK] (BERT semantics).
    /// </summary>
    private IEnumerable<int> TokenizeWord(string word)
    {
        if (word.Length == 0)
        {
            return Array.Empty<int>();
        }

        var pieces = new List<int>();
        var start = 0;

        while (start < word.Length)
        {
            var end = word.Length;
            int? matchedId = null;

            while (end > start)
            {
                var piece = word[start..end];
                if (start > 0)
                {
                    piece = ContinuationPrefix + piece;
                }

                if (_vocabulary.TryGetValue(piece, out var id))
                {
                    matchedId = id;
                    break;
                }

                end--;
            }

            if (matchedId == null)
            {
                return new[] { UnkTokenId };
            }

            pieces.Add(matchedId.Value);
            start = end;
        }

        return pieces;
    }

    private static void FlushCurrent(List<string> tokens, StringBuilder current)
    {
        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
            current.Clear();
        }
    }

    private static bool IsPunctuation(char ch)
    {
        return char.IsPunctuation(ch) || char.IsSymbol(ch);
    }
}
