using System.Globalization;
using System.Net;
using System.Text;

namespace QuickReplyKit.Core.Scripts;

/// <summary>
/// Interpreter for qrk-expr-1. This is an expression language, not a general-purpose runtime:
/// literals, fixed clock context, comparisons/boolean operators, and a closed set of pure functions.
/// It has no assignment, loops, user functions, members, reflection, I/O, process, environment,
/// clipboard, UI, native, regex, response lookup, or dynamic evaluation capability.
/// </summary>
public static class ScriptExpression
{
    public const int MaxSourceChars = 4_096;
    public const int MaxTokens = 512;
    public const int MaxAstNodes = 256;
    public const int MaxDepth = 32;
    public const int MaxEvaluationSteps = 1_024;
    public const int MaxArguments = 16;
    public const int MaxLiteralChars = 1_024;
    public const int MaxOutputChars = 8_192;

    private static readonly Limits DefaultLimits = new(
        MaxSourceChars, MaxTokens, MaxAstNodes, MaxDepth, MaxEvaluationSteps,
        MaxArguments, MaxLiteralChars, MaxOutputChars);

    public static ScriptOperationResult Validate(string? source)
    {
        try
        {
            _ = Parse(source, DefaultLimits);
            return ScriptOperationResult.Ok();
        }
        catch (ScriptException ex)
        {
            return ScriptOperationResult.Fail("", ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return ScriptOperationResult.Fail("", "invalid-expression", ex.Message);
        }
    }

    /// <summary>Evaluates against only the supplied clock and HTML-encodes the final scalar value.</summary>
    public static ScriptEvaluationResult Evaluate(string? source, DateTimeOffset now) =>
        Evaluate(source, now, DefaultLimits);

    internal static ScriptEvaluationResult Evaluate(string? source, DateTimeOffset now, Limits limits)
    {
        try
        {
            var node = Parse(source, limits);
            var context = new EvaluationContext(now, limits.MaxEvaluationSteps);
            string encoded = WebUtility.HtmlEncode(node.Evaluate(context).ToOutput());
            if (encoded.Length > limits.MaxOutputChars)
                throw new ScriptException("output-limit", $"Output exceeds {limits.MaxOutputChars} characters.");
            return ScriptEvaluationResult.Ok(encoded);
        }
        catch (ScriptException ex)
        {
            return ScriptEvaluationResult.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return ScriptEvaluationResult.Fail("evaluation-failed", ex.Message);
        }
    }

    internal readonly record struct Limits(
        int MaxSourceChars,
        int MaxTokens,
        int MaxAstNodes,
        int MaxDepth,
        int MaxEvaluationSteps,
        int MaxArguments,
        int MaxLiteralChars,
        int MaxOutputChars);

    private static Node Parse(string? source, Limits limits)
    {
        if (source is null) throw new ScriptException("invalid-expression", "The expression is null.");
        if (source.Length > limits.MaxSourceChars)
            throw new ScriptException("source-limit", $"Source exceeds {limits.MaxSourceChars} characters.");
        return new Parser(new Lexer(source, limits).ReadAll(), limits).Parse();
    }

    private sealed class ScriptException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }

    private enum TokenKind
    {
        String, Integer, True, False, Identifier,
        LeftParen, RightParen, Comma,
        Equal, NotEqual, Less, LessEqual, Greater, GreaterEqual,
        And, Or, Not, End,
    }

    private readonly record struct Token(TokenKind Kind, string Text, int Position);

    private sealed class Lexer(string source, Limits limits)
    {
        private int _position;

        public List<Token> ReadAll()
        {
            var tokens = new List<Token>();
            while (true)
            {
                SkipWhitespace();
                if (_position == source.Length) break;
                if (tokens.Count >= limits.MaxTokens)
                    throw new ScriptException("token-limit", $"Expression exceeds {limits.MaxTokens} tokens.");
                tokens.Add(ReadToken());
            }
            tokens.Add(new Token(TokenKind.End, "", _position));
            return tokens;
        }

        private Token ReadToken()
        {
            int start = _position;
            char c = source[_position];

            if (c == '"') return ReadString(start);
            if (char.IsAsciiDigit(c) || c == '-' && _position + 1 < source.Length && char.IsAsciiDigit(source[_position + 1]))
                return ReadInteger(start);
            if (char.IsAsciiLetter(c) || c == '_') return ReadIdentifier(start);

            _position++;
            return c switch
            {
                '(' => new(TokenKind.LeftParen, "(", start),
                ')' => new(TokenKind.RightParen, ")", start),
                ',' => new(TokenKind.Comma, ",", start),
                '=' when Take('=') => new(TokenKind.Equal, "==", start),
                '!' when Take('=') => new(TokenKind.NotEqual, "!=", start),
                '<' when Take('=') => new(TokenKind.LessEqual, "<=", start),
                '<' => new(TokenKind.Less, "<", start),
                '>' when Take('=') => new(TokenKind.GreaterEqual, ">=", start),
                '>' => new(TokenKind.Greater, ">", start),
                _ => throw new ScriptException("syntax", $"Unexpected character '{c}' at position {start}.")
            };
        }

        private Token ReadString(int start)
        {
            _position++;
            var value = new StringBuilder();
            while (_position < source.Length)
            {
                char c = source[_position++];
                if (c == '"')
                {
                    if (value.Length > limits.MaxLiteralChars)
                        throw new ScriptException("literal-limit", $"A literal exceeds {limits.MaxLiteralChars} characters.");
                    return new Token(TokenKind.String, value.ToString(), start);
                }
                if (char.IsControl(c))
                    throw new ScriptException("syntax", $"Control characters must be escaped at position {_position - 1}.");
                if (c != '\\')
                {
                    value.Append(c);
                    continue;
                }
                if (_position == source.Length)
                    throw new ScriptException("syntax", $"Unterminated escape at position {_position - 1}.");
                value.Append(source[_position++] switch
                {
                    '"' => '"',
                    '\\' => '\\',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    var escaped => throw new ScriptException("syntax", $"Unsupported escape '\\{escaped}'.")
                });
                if (value.Length > limits.MaxLiteralChars)
                    throw new ScriptException("literal-limit", $"A literal exceeds {limits.MaxLiteralChars} characters.");
            }
            throw new ScriptException("syntax", $"Unterminated string at position {start}.");
        }

        private Token ReadInteger(int start)
        {
            if (source[_position] == '-') _position++;
            while (_position < source.Length && char.IsAsciiDigit(source[_position])) _position++;
            return new Token(TokenKind.Integer, source[start.._position], start);
        }

        private Token ReadIdentifier(int start)
        {
            _position++;
            while (_position < source.Length &&
                   (char.IsAsciiLetterOrDigit(source[_position]) || source[_position] == '_')) _position++;
            string text = source[start.._position];
            TokenKind kind = text switch
            {
                "true" => TokenKind.True,
                "false" => TokenKind.False,
                "and" => TokenKind.And,
                "or" => TokenKind.Or,
                "not" => TokenKind.Not,
                _ => TokenKind.Identifier,
            };
            return new Token(kind, text, start);
        }

        private void SkipWhitespace()
        {
            while (_position < source.Length && char.IsWhiteSpace(source[_position])) _position++;
        }

        private bool Take(char expected)
        {
            if (_position >= source.Length || source[_position] != expected) return false;
            _position++;
            return true;
        }
    }

    private sealed class Parser(List<Token> tokens, Limits limits)
    {
        private static readonly HashSet<string> ContextNames =
            ["hour", "minute", "day", "month", "year", "weekday"];

        private static readonly HashSet<string> FunctionNames =
            ["if", "concat", "upper", "lower", "trim", "date", "time",
             "contains", "startsWith", "endsWith"];

        private int _position;
        private int _nodes;
        private int _nesting;

        public Node Parse()
        {
            var node = ParseOr();
            if (Current.Kind != TokenKind.End)
                throw new ScriptException("syntax", $"Unexpected token '{Current.Text}' at position {Current.Position}.");
            return node;
        }

        private Node ParseOr()
        {
            Node left = ParseAnd();
            while (Match(TokenKind.Or)) left = Add(new BinaryNode(TokenKind.Or, left, ParseAnd()));
            return left;
        }

        private Node ParseAnd()
        {
            Node left = ParseComparison();
            while (Match(TokenKind.And)) left = Add(new BinaryNode(TokenKind.And, left, ParseComparison()));
            return left;
        }

        private Node ParseComparison()
        {
            Node left = ParseUnary();
            if (Current.Kind is not (TokenKind.Equal or TokenKind.NotEqual or TokenKind.Less or
                TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual)) return left;
            TokenKind op = Advance().Kind;
            Node result = Add(new BinaryNode(op, left, ParseUnary()));
            if (Current.Kind is TokenKind.Equal or TokenKind.NotEqual or TokenKind.Less or
                TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual)
                throw new ScriptException("syntax", "Chained comparisons are not supported.");
            return result;
        }

        private Node ParseUnary()
        {
            if (!Match(TokenKind.Not)) return ParsePrimary();
            EnterNesting();
            try { return Add(new NotNode(ParseUnary())); }
            finally { _nesting--; }
        }

        private Node ParsePrimary()
        {
            Token token = Advance();
            switch (token.Kind)
            {
                case TokenKind.String:
                    return Add(new LiteralNode(Value.From(token.Text)));
                case TokenKind.Integer:
                    if (!int.TryParse(token.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int integer))
                        throw new ScriptException("literal", $"Integer '{token.Text}' is outside the 32-bit range.");
                    return Add(new LiteralNode(Value.From(integer)));
                case TokenKind.True:
                    return Add(new LiteralNode(Value.From(true)));
                case TokenKind.False:
                    return Add(new LiteralNode(Value.From(false)));
                case TokenKind.Identifier:
                    return Current.Kind == TokenKind.LeftParen ? ParseCall(token) : ParseContext(token);
                case TokenKind.LeftParen:
                    EnterNesting();
                    try
                    {
                        Node grouped = ParseOr();
                        Expect(TokenKind.RightParen, "Expected ')'.");
                        return grouped;
                    }
                    finally { _nesting--; }
                default:
                    throw new ScriptException("syntax", $"Expected an expression at position {token.Position}.");
            }
        }

        private Node ParseContext(Token token)
        {
            if (!ContextNames.Contains(token.Text))
                throw new ScriptException("denied-capability", $"Identifier '{token.Text}' is not allowed.");
            return Add(new ContextNode(token.Text));
        }

        private Node ParseCall(Token token)
        {
            if (!FunctionNames.Contains(token.Text))
                throw new ScriptException("denied-capability", $"Function '{token.Text}' is not allowed.");

            Advance();
            EnterNesting();
            try
            {
                var arguments = new List<Node>();
                if (Current.Kind != TokenKind.RightParen)
                {
                    while (true)
                    {
                        if (arguments.Count >= limits.MaxArguments)
                            throw new ScriptException("argument-limit", $"A call exceeds {limits.MaxArguments} arguments.");
                        arguments.Add(ParseOr());
                        if (!Match(TokenKind.Comma)) break;
                    }
                }
                Expect(TokenKind.RightParen, "Expected ')' after function arguments.");
                return Add(new CallNode(token.Text, arguments));
            }
            finally { _nesting--; }
        }

        private T Add<T>(T node) where T : Node
        {
            if (++_nodes > limits.MaxAstNodes)
                throw new ScriptException("ast-limit", $"Expression exceeds {limits.MaxAstNodes} AST nodes.");
            if (node.Depth > limits.MaxDepth)
                throw new ScriptException("depth-limit", $"Expression exceeds depth {limits.MaxDepth}.");
            return node;
        }

        private void EnterNesting()
        {
            if (++_nesting > limits.MaxDepth)
                throw new ScriptException("depth-limit", $"Expression exceeds depth {limits.MaxDepth}.");
        }

        private Token Current => tokens[_position];
        private Token Advance() => tokens[_position++];

        private bool Match(TokenKind kind)
        {
            if (Current.Kind != kind) return false;
            _position++;
            return true;
        }

        private void Expect(TokenKind kind, string error)
        {
            if (!Match(kind)) throw new ScriptException("syntax", error);
        }
    }

    private enum ValueKind { String, Integer, Boolean }

    private readonly record struct Value(ValueKind Kind, string? String, int Integer, bool Boolean)
    {
        public static Value From(string value) => new(ValueKind.String, value, 0, false);
        public static Value From(int value) => new(ValueKind.Integer, null, value, false);
        public static Value From(bool value) => new(ValueKind.Boolean, null, 0, value);

        public string RequireString(string function) => Kind == ValueKind.String
            ? String!
            : throw new ScriptException("type", $"{function} requires string arguments.");

        public bool RequireBoolean(string operation) => Kind == ValueKind.Boolean
            ? Boolean
            : throw new ScriptException("type", $"{operation} requires boolean arguments.");

        public string ToOutput() => Kind switch
        {
            ValueKind.String => String!,
            ValueKind.Integer => Integer.ToString(CultureInfo.InvariantCulture),
            _ => Boolean ? "true" : "false",
        };
    }

    private sealed class EvaluationContext(DateTimeOffset now, int maxSteps)
    {
        private int _steps;
        public DateTimeOffset Now { get; } = now;

        public void Step()
        {
            if (++_steps > maxSteps)
                throw new ScriptException("evaluation-limit", $"Evaluation exceeds {maxSteps} steps.");
        }
    }

    private abstract class Node(int depth)
    {
        public int Depth { get; } = depth;
        public abstract Value Evaluate(EvaluationContext context);
    }

    private sealed class LiteralNode(Value value) : Node(1)
    {
        public override Value Evaluate(EvaluationContext context)
        {
            context.Step();
            return value;
        }
    }

    private sealed class ContextNode(string name) : Node(1)
    {
        public override Value Evaluate(EvaluationContext context)
        {
            context.Step();
            return name switch
            {
                "hour" => Value.From(context.Now.Hour),
                "minute" => Value.From(context.Now.Minute),
                "day" => Value.From(context.Now.Day),
                "month" => Value.From(context.Now.Month),
                "year" => Value.From(context.Now.Year),
                "weekday" => Value.From(context.Now.DayOfWeek.ToString()),
                _ => throw new ScriptException("denied-capability", $"Context '{name}' is not allowed."),
            };
        }
    }

    private sealed class NotNode(Node operand) : Node(operand.Depth + 1)
    {
        public override Value Evaluate(EvaluationContext context)
        {
            context.Step();
            return Value.From(!operand.Evaluate(context).RequireBoolean("not"));
        }
    }

    private sealed class BinaryNode(TokenKind operation, Node left, Node right)
        : Node(Math.Max(left.Depth, right.Depth) + 1)
    {
        public override Value Evaluate(EvaluationContext context)
        {
            context.Step();
            if (operation is TokenKind.And or TokenKind.Or)
            {
                bool first = left.Evaluate(context).RequireBoolean(operation == TokenKind.And ? "and" : "or");
                if (operation == TokenKind.And && !first) return Value.From(false);
                if (operation == TokenKind.Or && first) return Value.From(true);
                return Value.From(right.Evaluate(context).RequireBoolean(operation == TokenKind.And ? "and" : "or"));
            }

            Value a = left.Evaluate(context);
            Value b = right.Evaluate(context);
            if (a.Kind != b.Kind)
                throw new ScriptException("type", "Comparisons require operands of the same type.");

            if (operation is TokenKind.Equal or TokenKind.NotEqual)
            {
                bool equal = a.Kind switch
                {
                    ValueKind.String => string.Equals(a.String, b.String, StringComparison.Ordinal),
                    ValueKind.Integer => a.Integer == b.Integer,
                    _ => a.Boolean == b.Boolean,
                };
                return Value.From(operation == TokenKind.Equal ? equal : !equal);
            }

            if (a.Kind != ValueKind.Integer)
                throw new ScriptException("type", "Ordering comparisons require integers.");
            return Value.From(operation switch
            {
                TokenKind.Less => a.Integer < b.Integer,
                TokenKind.LessEqual => a.Integer <= b.Integer,
                TokenKind.Greater => a.Integer > b.Integer,
                TokenKind.GreaterEqual => a.Integer >= b.Integer,
                _ => throw new ScriptException("invalid-expression", "Unknown comparison operator."),
            });
        }
    }

    private sealed class CallNode(string name, IReadOnlyList<Node> arguments)
        : Node((arguments.Count == 0 ? 0 : arguments.Max(argument => argument.Depth)) + 1)
    {
        public override Value Evaluate(EvaluationContext context)
        {
            context.Step();
            return name switch
            {
                "if" => If(context),
                "concat" => Value.From(string.Concat(arguments.Select(argument => argument.Evaluate(context).ToOutput()))),
                "upper" => Value.From(OneString(context).ToUpperInvariant()),
                "lower" => Value.From(OneString(context).ToLowerInvariant()),
                "trim" => Value.From(OneString(context).Trim()),
                "date" => NoArguments(context.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                "time" => NoArguments(context.Now.ToString("HH:mm", CultureInfo.InvariantCulture)),
                "contains" => TwoStrings(context, static (a, b) => a.Contains(b, StringComparison.Ordinal)),
                "startsWith" => TwoStrings(context, static (a, b) => a.StartsWith(b, StringComparison.Ordinal)),
                "endsWith" => TwoStrings(context, static (a, b) => a.EndsWith(b, StringComparison.Ordinal)),
                _ => throw new ScriptException("denied-capability", $"Function '{name}' is not allowed."),
            };
        }

        private Value If(EvaluationContext context)
        {
            RequireCount(3);
            bool condition = arguments[0].Evaluate(context).RequireBoolean("if");
            return arguments[condition ? 1 : 2].Evaluate(context);
        }

        private string OneString(EvaluationContext context)
        {
            RequireCount(1);
            return arguments[0].Evaluate(context).RequireString(name);
        }

        private Value TwoStrings(EvaluationContext context, Func<string, string, bool> operation)
        {
            RequireCount(2);
            string first = arguments[0].Evaluate(context).RequireString(name);
            string second = arguments[1].Evaluate(context).RequireString(name);
            return Value.From(operation(first, second));
        }

        private Value NoArguments(string value)
        {
            RequireCount(0);
            return Value.From(value);
        }

        private void RequireCount(int count)
        {
            if (arguments.Count != count)
                throw new ScriptException("argument-count", $"{name} requires {count} arguments.");
        }
    }
}
