using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

public enum TokenType
{
    Identifier,
    Symbol,
    String,
    Number
}

public readonly struct Token
{
    public readonly int line;
    public readonly TokenType type;
    public readonly string value;

    public Token(int line, TokenType type, string value)
    {
        this.line = line;
        this.type = type;
        this.value = value;
    }

    public override string ToString() => $"{line} {type} {value}";
}

public class TokenSet
{
    public readonly List<Token> tokens;
    private int id;
    private Stack<int> saved = new();

    public TokenSet(List<Token> tokens)
    {
        this.tokens = tokens;
        id = 0;
    }
    public void Push()
    {
        saved.Push(id);
    }
    public void Compress()
    {
        saved.Pop();
    }
    public void Pop()
    {
        id = saved.Pop();
    }
    private Token SafeGet(int idx)
    {
        if (idx < 0 || idx >= tokens.Count) throw new Exception("Token index out of range");
        //Console.WriteLine($"SafeGet {idx}: {tokens[idx].value} ({tokens[idx].type})");
        return tokens[idx];
    }
    public bool Safe()
    {
        return id < tokens.Count;
    }
    public Token Get(bool advance = true)
    {
        if (advance)
            return SafeGet(id++);
        return SafeGet(id);
    }
    public string Identifier(out int line)
    {
        var t = Get();
        if (t.type != TokenType.Identifier) throw new Exception("Expected identifier");
        line = t.line;
        return t.value;
    }
    public string IdentifierSafe(out int line)
    {
        var t = Get();
        line = t.line;
        if (t.type != TokenType.Identifier) return "";
        return t.value;
    }
    public void Identifier(string expected)
    {
        var t = Get();
        if (t.type != TokenType.Identifier || t.value != expected) throw new Exception($"Expected identifier '{expected}' but got '{t.value}'");
    }
    public bool IsIdentifier(string expected)
    {
        var t = Get(false);
        if (t.type != TokenType.Identifier || t.value != expected) return false;
        id++;
        return true;
    }

    public bool IsIdentifier(string expected, out int line)
    {
        var t = Get(false);
        line = t.line;
        if (t.type != TokenType.Identifier || t.value != expected) return false;
        id++;
        return true;
    }

    public bool SymbolSafe(out string value, out int line)
    {
        var t = Get(false);
        value = t.value;
        line = t.line;
        if (t.type != TokenType.Symbol) return false;
        id++;
        return true;
    }


    public void Symbol(string expected)
    {
        var t = Get();
        if (t.type != TokenType.Symbol || t.value != expected) throw new Exception($"Expected symbol '{expected}' but got '{t.value}'");
    }
    public bool IsSymbol(params string[] expected)
    {
        var t = Get(false);
        if (t.type != TokenType.Symbol || !expected.Contains(t.value)) return false;
        id++;
        return true;
    }
    public bool IsSymbol(out string value, params string[] expected)
    {
        var t = Get(false);
        value = t.value;
        if (t.type != TokenType.Symbol || !expected.Contains(t.value)) return false;
        id++;
        return true;
    }
    public bool IsSymbol(string expected, out int line)
    {
        var t = Get(false);
        line = t.line;
        if (t.type != TokenType.Symbol || t.value != expected) return false;
        id++;
        return true;
    }
}

public static class Tokenizer
{
    static readonly string[] Symbols = new[]
    {
        "==", "!=", "<=", ">=", "&&", "||", "??", "=>",
        "<<", ">>", "..",

        "{", "}", "(", ")", "[", "]",
        ";", ":", ",", ".",
        "+", "-", "*", "/", "%", "!",
        "=", "<", ">", "&", "|", "^", "~",
        "?", "@", "$"
    };

    static readonly Dictionary<char, List<string>> SymbolsByFirstChar = BuildSymbolIndex(Symbols);
    static readonly int MaxSymbolLength = GetMaxLen(Symbols);

    public static TokenSet Tokenize(string text)
    {
        if (text == null) throw new ArgumentNullException(nameof(text));

        var tokens = new List<Token>(Math.Max(16, text.Length / 8));

        int idx = 0;
        int line = 1;

        while (idx < text.Length)
        {
            char c = text[idx];

            if (IsWhitespace(c))
            {
                ConsumeWhitespace(text, ref idx, ref line);
                continue;
            }

            if (c == '/' && idx + 1 < text.Length)
            {
                char next = text[idx + 1];
                if (next == '/')
                {
                    idx += 2;
                    while (idx < text.Length && text[idx] != '\n') idx++;
                    continue;
                }
                if (next == '*')
                {
                    idx += 2;
                    while (idx < text.Length)
                    {
                        if (text[idx] == '\n') line++;
                        if (text[idx] == '*' && idx + 1 < text.Length && text[idx + 1] == '/')
                        {
                            idx += 2;
                            break;
                        }
                        idx++;
                    }
                    continue;
                }
            }

            int tokenLine = line;

            if (c == '"')
            {
                var value = ReadString(text, ref idx, ref line);
                tokens.Add(new Token(tokenLine, TokenType.String, value));
                continue;
            }

            if (LooksLikeNumberStart(text, idx))
            {
                var value = ReadNumber(text, ref idx);
                tokens.Add(new Token(tokenLine, TokenType.Number, value));
                continue;
            }

            if (LooksLikeIdentifierStart(text, idx))
            {
                var value = ReadIdentifier(text, ref idx);
                tokens.Add(new Token(tokenLine, TokenType.Identifier, value));
                continue;
            }

            var sym = TryReadSymbol(text, ref idx);
            if (sym != null)
            {
                tokens.Add(new Token(tokenLine, TokenType.Symbol, sym));
                continue;
            }

            throw new Exception($"Tokenizer error on line {line}: unexpected character '{c}' (0x{((int)c):X2})");
        }

        return new TokenSet(tokens);
    }

    static void ConsumeWhitespace(string text, ref int idx, ref int line)
    {
        while (idx < text.Length)
        {
            char c = text[idx];
            if (!IsWhitespace(c)) return;

            if (c == '\r')
            {
                if (idx + 1 < text.Length && text[idx + 1] == '\n') idx++;
                line++;
            }
            else if (c == '\n')
            {
                line++;
            }

            idx++;
        }
    }

    static bool IsWhitespace(char c)
    {
        return c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f' || c == '\v';
    }

    static bool LooksLikeIdentifierStart(string text, int idx)
    {
        char c = text[idx];

        return IsLetter(c);
    }

    static string ReadIdentifier(string text, ref int idx)
    {
        int start = idx;

        if (idx < text.Length && IsLetter(text[idx]))
        {
            idx++;
        }
        else
        {
            throw new Exception("Invalid identifier start");
        }

        while (idx < text.Length)
        {
            char ch = text[idx];
            if (IsLetter(ch) || IsDigit(ch))
            {
                idx++;
                continue;
            }
            break;
        }

        return text.Substring(start, idx - start);
    }

    static bool LooksLikeNumberStart(string text, int idx)
    {
        char c = text[idx];

        if (IsDigit(c)) return true;

        if (c == '.')
        {
            return idx + 1 < text.Length && IsDigit(text[idx + 1]);
        }

        if (c == '-')
        {
            if (idx + 1 >= text.Length) return false;
            char n = text[idx + 1];

            if (IsDigit(n)) return true;
            if (n == '.')
            {
                return idx + 2 < text.Length && IsDigit(text[idx + 2]);
            }

            if (n == '0' && idx + 2 < text.Length)
            {
                char b = text[idx + 2];
                return b == 'x' || b == 'X' || b == 'b' || b == 'B';
            }

            return false;
        }

        return false;
    }

    static string ReadNumber(string text, ref int idx)
    {
        int start = idx;

        bool hasSign = false;
        if (text[idx] == '-')
        {
            hasSign = true;
            idx++;
        }

        if (idx + 1 < text.Length && text[idx] == '0')
        {
            char p = text[idx + 1];
            if (p == 'x' || p == 'X')
            {
                idx += 2;
                int digitsStart = idx;
                while (idx < text.Length && IsHexDigit(text[idx])) idx++;

                if (idx == digitsStart) throw new Exception("Invalid hex number: expected at least one hex digit");
                return text.Substring(start, idx - start);
            }

            if (p == 'b' || p == 'B')
            {
                idx += 2;
                int digitsStart = idx;
                while (idx < text.Length && (text[idx] == '0' || text[idx] == '1')) idx++;

                if (idx == digitsStart) throw new Exception("Invalid binary number: expected at least one binary digit");
                return text.Substring(start, idx - start);
            }
        }

        bool sawDigitsBeforeDot = false;

        while (idx < text.Length && IsDigit(text[idx]))
        {
            sawDigitsBeforeDot = true;
            idx++;
        }

        bool sawDot = false;
        if (idx < text.Length && text[idx] == '.')
        {
            sawDot = true;
            idx++;

            while (idx < text.Length && IsDigit(text[idx]))
            {
                idx++;
            }
        }

        if (!sawDigitsBeforeDot && !sawDot)
        {
            throw new Exception("Invalid number");
        }

        int len = idx - start;
        if (len <= 0) throw new Exception("Invalid number");

        if (hasSign && len == 1) throw new Exception("Invalid number: '-' alone");
        if (text[start] == '.' && len == 1) throw new Exception("Invalid number: '.' alone");
        if (hasSign && text[start + 1] == '.' && len == 2) throw new Exception("Invalid number: '-.'");

        return text.Substring(start, idx - start);
    }

    static string ReadString(string text, ref int idx, ref int line)
    {
        if (text[idx] != '"') throw new Exception("ReadString called at non-quote");

        idx++;
        var chars = new List<char>(64);

        while (idx < text.Length)
        {
            char c = text[idx];

            if (c == '"')
            {
                idx++;
                return new string(chars.ToArray());
            }

            if (c == '\r')
            {
                if (idx + 1 < text.Length && text[idx + 1] == '\n') idx++;
                line++;
                chars.Add('\n');
                idx++;
                continue;
            }

            if (c == '\n')
            {
                line++;
                chars.Add('\n');
                idx++;
                continue;
            }

            if (c == '\\')
            {
                idx++;
                if (idx >= text.Length) throw new Exception($"Unterminated escape sequence in string on line {line}");

                char e = text[idx];

                if (e == '\\') { chars.Add('\\'); idx++; continue; }
                if (e == '"') { chars.Add('"'); idx++; continue; }
                if (e == 'n') { chars.Add('\n'); idx++; continue; }
                if (e == 'r') { chars.Add('\r'); idx++; continue; }
                if (e == 't') { chars.Add('\t'); idx++; continue; }

                if (e == '0')
                {
                    if (idx + 3 < text.Length && (text[idx + 1] == 'x' || text[idx + 1] == 'X'))
                    {
                        char h1 = text[idx + 2];
                        char h2 = text[idx + 3];
                        if (!IsHexDigit(h1) || !IsHexDigit(h2))
                        {
                            throw new Exception("Invalid \\0xHH escape in string");
                        }

                        int value = (HexValue(h1) << 4) | HexValue(h2);
                        chars.Add((char)value);
                        idx += 4;
                        continue;
                    }
                }

                throw new Exception("Unknown escape sequence \\{e} in string");
            }

            chars.Add(c);
            idx++;
        }

        throw new Exception("Unterminated string literal on");
    }

    static string? TryReadSymbol(string text, ref int idx)
    {
        char c = text[idx];

        if (!SymbolsByFirstChar.TryGetValue(c, out var list))
        {
            return null;
        }

        int maxLen = Math.Min(MaxSymbolLength, text.Length - idx);

        for (int len = maxLen; len >= 1; len--)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var sym = list[i];
                if (sym.Length != len) continue;

                if (Matches(text, idx, sym))
                {
                    idx += sym.Length;
                    return sym;
                }
            }
        }

        return null;
    }

    static bool Matches(string text, int idx, string sym)
    {
        if (idx + sym.Length > text.Length) return false;
        for (int i = 0; i < sym.Length; i++)
        {
            if (text[idx + i] != sym[i]) return false;
        }
        return true;
    }

    static bool IsLetter(char c)
    {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
    }

    static bool IsDigit(char c)
    {
        return c >= '0' && c <= '9';
    }

    static bool IsHexDigit(char c)
    {
        return (c >= '0' && c <= '9')
            || (c >= 'a' && c <= 'f')
            || (c >= 'A' && c <= 'F');
    }

    static int HexValue(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return 10 + (c - 'a');
        if (c >= 'A' && c <= 'F') return 10 + (c - 'A');
        throw new Exception("Not a hex digit");
    }

    static Dictionary<char, List<string>> BuildSymbolIndex(string[] symbols)
    {
        var dict = new Dictionary<char, List<string>>();
        for (int i = 0; i < symbols.Length; i++)
        {
            var s = symbols[i];
            if (string.IsNullOrEmpty(s)) continue;

            char first = s[0];
            if (!dict.TryGetValue(first, out var list))
            {
                list = new List<string>();
                dict[first] = list;
            }

            list.Add(s);
        }

        return dict;
    }

    static int GetMaxLen(string[] symbols)
    {
        int max = 0;
        for (int i = 0; i < symbols.Length; i++)
        {
            var s = symbols[i];
            if (s != null && s.Length > max) max = s.Length;
        }
        return max;
    }
}
