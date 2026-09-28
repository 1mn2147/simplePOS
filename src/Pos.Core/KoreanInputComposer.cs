using System.Text;

namespace Pos.Core;

public sealed class KoreanInputComposer
{
    private const int HangulBase = 0xAC00;
    private static readonly char[] Initials =
    [
        'ㄱ', 'ㄲ', 'ㄴ', 'ㄷ', 'ㄸ', 'ㄹ', 'ㅁ', 'ㅂ', 'ㅃ', 'ㅅ',
        'ㅆ', 'ㅇ', 'ㅈ', 'ㅉ', 'ㅊ', 'ㅋ', 'ㅌ', 'ㅍ', 'ㅎ',
    ];

    private static readonly char[] Medials =
    [
        'ㅏ', 'ㅐ', 'ㅑ', 'ㅒ', 'ㅓ', 'ㅔ', 'ㅕ', 'ㅖ', 'ㅗ', 'ㅘ',
        'ㅙ', 'ㅚ', 'ㅛ', 'ㅜ', 'ㅝ', 'ㅞ', 'ㅟ', 'ㅠ', 'ㅡ', 'ㅢ', 'ㅣ',
    ];

    private static readonly char[] Finals =
    [
        '\0', 'ㄱ', 'ㄲ', 'ㄳ', 'ㄴ', 'ㄵ', 'ㄶ', 'ㄷ', 'ㄹ', 'ㄺ',
        'ㄻ', 'ㄼ', 'ㄽ', 'ㄾ', 'ㄿ', 'ㅀ', 'ㅁ', 'ㅂ', 'ㅄ', 'ㅅ',
        'ㅆ', 'ㅇ', 'ㅈ', 'ㅊ', 'ㅋ', 'ㅌ', 'ㅍ', 'ㅎ',
    ];

    private static readonly Dictionary<(char First, char Second), char> CompoundMedials = new()
    {
        [('ㅗ', 'ㅏ')] = 'ㅘ',
        [('ㅗ', 'ㅐ')] = 'ㅙ',
        [('ㅗ', 'ㅣ')] = 'ㅚ',
        [('ㅜ', 'ㅓ')] = 'ㅝ',
        [('ㅜ', 'ㅔ')] = 'ㅞ',
        [('ㅜ', 'ㅣ')] = 'ㅟ',
        [('ㅡ', 'ㅣ')] = 'ㅢ',
    };

    private static readonly Dictionary<char, (char First, char Second)> SplitMedials =
        CompoundMedials.ToDictionary(pair => pair.Value, pair => pair.Key);

    private static readonly Dictionary<(char First, char Second), char> CompoundFinals = new()
    {
        [('ㄱ', 'ㅅ')] = 'ㄳ',
        [('ㄴ', 'ㅈ')] = 'ㄵ',
        [('ㄴ', 'ㅎ')] = 'ㄶ',
        [('ㄹ', 'ㄱ')] = 'ㄺ',
        [('ㄹ', 'ㅁ')] = 'ㄻ',
        [('ㄹ', 'ㅂ')] = 'ㄼ',
        [('ㄹ', 'ㅅ')] = 'ㄽ',
        [('ㄹ', 'ㅌ')] = 'ㄾ',
        [('ㄹ', 'ㅍ')] = 'ㄿ',
        [('ㄹ', 'ㅎ')] = 'ㅀ',
        [('ㅂ', 'ㅅ')] = 'ㅄ',
    };

    private static readonly Dictionary<char, (char First, char Second)> SplitFinals =
        CompoundFinals.ToDictionary(pair => pair.Value, pair => pair.Key);

    private readonly StringBuilder _committed = new();
    private char? _initial;
    private char? _medial;
    private char? _final;

    public KoreanInputComposer(string? initialText = null)
    {
        if (!string.IsNullOrEmpty(initialText))
        {
            _committed.Append(initialText);
        }
    }

    public string Text => _committed.ToString() + (ComposeCurrent()?.ToString() ?? string.Empty);

    public string Input(char key, bool shift = false)
    {
        var jamo = MapTwoBeolsik(key, shift || char.IsUpper(key));
        if (jamo is null)
        {
            CommitActive();
            _committed.Append(key);
            return Text;
        }

        return InputJamo(jamo.Value);
    }

    public string InputJamo(char jamo)
    {
        if (Array.IndexOf(Medials, jamo) >= 0)
        {
            InputVowel(jamo);
        }
        else if (Array.IndexOf(Initials, jamo) >= 0 || Array.IndexOf(Finals, jamo) > 0)
        {
            InputConsonant(jamo);
        }
        else
        {
            CommitActive();
            _committed.Append(jamo);
        }

        return Text;
    }

    public string Backspace()
    {
        if (_final.HasValue)
        {
            _final = SplitFinals.TryGetValue(_final.Value, out var split) ? split.First : null;
        }
        else if (_medial.HasValue)
        {
            _medial = SplitMedials.TryGetValue(_medial.Value, out var split) ? split.First : null;
        }
        else if (_initial.HasValue)
        {
            _initial = null;
        }
        else if (_committed.Length > 0)
        {
            _committed.Length--;
        }

        return Text;
    }

    public string Space()
    {
        CommitActive();
        _committed.Append(' ');
        return Text;
    }

    public string Commit()
    {
        CommitActive();
        return Text;
    }

    public string SetText(string? text)
    {
        Clear();
        if (!string.IsNullOrEmpty(text))
        {
            _committed.Append(text);
        }

        return Text;
    }

    public void Clear()
    {
        _committed.Clear();
        ResetActive();
    }

    private void InputConsonant(char consonant)
    {
        if (!_initial.HasValue && !_medial.HasValue)
        {
            _initial = consonant;
            return;
        }

        if (!_initial.HasValue && _medial.HasValue)
        {
            CommitActive();
            _initial = consonant;
            return;
        }

        if (_initial.HasValue && !_medial.HasValue)
        {
            CommitActive();
            _initial = consonant;
            return;
        }

        if (!_final.HasValue)
        {
            if (Array.IndexOf(Finals, consonant) > 0)
            {
                _final = consonant;
            }
            else
            {
                CommitActive();
                _initial = consonant;
            }

            return;
        }

        if (CompoundFinals.TryGetValue((_final.Value, consonant), out var compound))
        {
            _final = compound;
            return;
        }

        CommitActive();
        _initial = consonant;
    }

    private void InputVowel(char vowel)
    {
        if (!_initial.HasValue && !_medial.HasValue)
        {
            _medial = vowel;
            return;
        }

        if (!_initial.HasValue && _medial.HasValue)
        {
            if (CompoundMedials.TryGetValue((_medial.Value, vowel), out var compound))
            {
                _medial = compound;
            }
            else
            {
                CommitActive();
                _medial = vowel;
            }

            return;
        }

        if (_initial.HasValue && !_medial.HasValue)
        {
            _medial = vowel;
            return;
        }

        if (!_final.HasValue)
        {
            if (CompoundMedials.TryGetValue((_medial!.Value, vowel), out var compound))
            {
                _medial = compound;
            }
            else
            {
                CommitActive();
                _medial = vowel;
            }

            return;
        }

        var previousFinal = _final.Value;
        if (SplitFinals.TryGetValue(previousFinal, out var splitFinal))
        {
            _final = splitFinal.First;
            CommitActive();
            _initial = splitFinal.Second;
            _medial = vowel;
        }
        else
        {
            _final = null;
            CommitActive();
            _initial = previousFinal;
            _medial = vowel;
        }
    }

    private void CommitActive()
    {
        var current = ComposeCurrent();
        if (current.HasValue)
        {
            _committed.Append(current.Value);
        }

        ResetActive();
    }

    private char? ComposeCurrent()
    {
        if (_initial.HasValue && _medial.HasValue)
        {
            var initialIndex = Array.IndexOf(Initials, _initial.Value);
            var medialIndex = Array.IndexOf(Medials, _medial.Value);
            var finalIndex = _final.HasValue ? Array.IndexOf(Finals, _final.Value) : 0;
            if (initialIndex >= 0 && medialIndex >= 0 && finalIndex >= 0)
            {
                return (char)(HangulBase + ((initialIndex * Medials.Length + medialIndex) * Finals.Length) + finalIndex);
            }
        }

        return _initial ?? _medial;
    }

    private void ResetActive()
    {
        _initial = null;
        _medial = null;
        _final = null;
    }

    private static char? MapTwoBeolsik(char key, bool shift)
    {
        return char.ToLowerInvariant(key) switch
        {
            'r' => shift ? 'ㄲ' : 'ㄱ',
            's' => 'ㄴ',
            'e' => shift ? 'ㄸ' : 'ㄷ',
            'f' => 'ㄹ',
            'a' => 'ㅁ',
            'q' => shift ? 'ㅃ' : 'ㅂ',
            't' => shift ? 'ㅆ' : 'ㅅ',
            'd' => 'ㅇ',
            'w' => shift ? 'ㅉ' : 'ㅈ',
            'c' => 'ㅊ',
            'z' => 'ㅋ',
            'x' => 'ㅌ',
            'v' => 'ㅍ',
            'g' => 'ㅎ',
            'k' => 'ㅏ',
            'o' => shift ? 'ㅒ' : 'ㅐ',
            'i' => 'ㅑ',
            'j' => 'ㅓ',
            'p' => shift ? 'ㅖ' : 'ㅔ',
            'u' => 'ㅕ',
            'h' => 'ㅗ',
            'y' => 'ㅛ',
            'n' => 'ㅜ',
            'b' => 'ㅠ',
            'm' => 'ㅡ',
            'l' => 'ㅣ',
            _ => null,
        };
    }
}
