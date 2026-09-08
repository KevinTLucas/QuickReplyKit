using QuickReplyKit.Core.Scripts;

namespace QuickReplyKit.Tests;

public class ScriptExpressionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 7, 14, 5, 6, TimeSpan.FromHours(-5)); // Monday

    [Theory]
    [InlineData("\"hello\"", "hello")]
    [InlineData("-42", "-42")]
    [InlineData("true", "true")]
    [InlineData("hour", "14")]
    [InlineData("minute", "5")]
    [InlineData("day", "7")]
    [InlineData("month", "9")]
    [InlineData("year", "2026")]
    [InlineData("weekday", "Monday")]
    [InlineData("date()", "2026-09-07")]
    [InlineData("time()", "14:05")]
    [InlineData("upper(\"Mix\")", "MIX")]
    [InlineData("lower(\"Mix\")", "mix")]
    [InlineData("trim(\"  x  \")", "x")]
    [InlineData("concat(\"Y\", year, true)", "Y2026true")]
    [InlineData("contains(\"alphabet\", \"pha\")", "true")]
    [InlineData("startsWith(\"alphabet\", \"alpha\")", "true")]
    [InlineData("endsWith(\"alphabet\", \"bet\")", "true")]
    [InlineData("if(hour < 12, \"AM\", \"PM\")", "PM")]
    public void Evaluate_AllowedVocabulary_IsDeterministic(string expression, string expected)
    {
        var result = ScriptExpression.Evaluate(expression, Now);

        Assert.True(result.Success, result.Error);
        Assert.Equal(expected, result.Output);
    }

    [Theory]
    [InlineData("1 == 1", "true")]
    [InlineData("1 != 2", "true")]
    [InlineData("1 < 2", "true")]
    [InlineData("2 <= 2", "true")]
    [InlineData("3 > 2", "true")]
    [InlineData("3 >= 3", "true")]
    [InlineData("true and not false", "true")]
    [InlineData("false or true and false", "false")]
    [InlineData("(false or true) and true", "true")]
    public void Evaluate_Operators_HaveFixedSemantics(string expression, string expected)
    {
        Assert.Equal(expected, ScriptExpression.Evaluate(expression, Now).Output);
    }

    [Fact]
    public void Evaluate_If_IsLazy()
    {
        var result = ScriptExpression.Evaluate("if(true, \"ok\", upper(1))", Now);

        Assert.True(result.Success, result.Error);
        Assert.Equal("ok", result.Output);
    }

    [Theory]
    [InlineData("upper()")]
    [InlineData("1 and true")]
    [InlineData("contains(\"x\")")]
    [InlineData("if(true, \"text\", 1)")]
    [InlineData("if(true, \"ok\", upper(1))")]
    public void Validate_RejectsStaticallyInvalidArityAndTypes(string expression)
    {
        Assert.False(ScriptExpression.Validate(expression).Success);
    }

    [Fact]
    public void Validate_AcceptsWellTypedExpression()
    {
        Assert.True(ScriptExpression.Validate("if(hour < 12, upper(\"am\"), concat(\"p\", \"m\"))").Success);
    }

    [Theory]
    [InlineData("not true == false", "true")]       // (not true) == false
    [InlineData("true or false and false", "true")] // true or (false and false)
    [InlineData("not false and false", "false")]    // (not false) and false
    public void Operators_PrecedenceIsDocumentedAndStable(string expression, string expected)
    {
        Assert.True(ScriptExpression.Validate(expression).Success);
        Assert.Equal(expected, ScriptExpression.Evaluate(expression, Now).Output);
    }

    [Fact]
    public void Operators_NotBindsBeforeComparison()
    {
        var result = ScriptExpression.Validate("not 1 == 2");

        Assert.False(result.Success);
        Assert.Equal("[script error: type]", result.Marker);
    }

    [Fact]
    public void Evaluate_HtmlEncodesOnlyAtOutputBoundary()
    {
        var result = ScriptExpression.Evaluate("concat(\"<b>\", \"&\\\"\")", Now);

        Assert.True(result.Success, result.Error);
        Assert.Equal("&lt;b&gt;&amp;&quot;", result.Output);
    }

    [Theory]
    [InlineData("while(true)")]                         // loops
    [InlineData("recurse()")]                          // recursion
    [InlineData("userFunction(1)")]                    // user functions
    [InlineData("eval(\"1\")")]                       // eval / dynamic execution
    [InlineData("System.IO.File.ReadAllText(\"x\")")] // C#, member access, files
    [InlineData("Get-Content(\"x\")")]                // PowerShell
    [InlineData("process.env.PATH")]                   // JavaScript, process, environment
    [InlineData("httpGet(\"https://example.com\")")]  // network
    [InlineData("registry(\"HKCU\")")]                // registry
    [InlineData("clipboard()")]
    [InlineData("showWindow()")]
    [InlineData("comObject(\"x\")")]
    [InlineData("loadNative(\"x.dll\")")]
    [InlineData("regex(\"x\", \".*\")")]
    [InlineData("response(\"Other response\")")]
    [InlineData("reflect(\"System.String\")")]
    public void Evaluate_DeniedCapabilities_FailClosed(string expression)
    {
        var result = ScriptExpression.Evaluate(expression, Now);

        Assert.False(result.Success);
        Assert.StartsWith("[script error:", result.Output);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("(")]
    [InlineData("true false")]
    [InlineData("1 = 1")]
    [InlineData("1 < 2 < 3")]
    [InlineData("\"unterminated")]
    [InlineData("\"bad\\q\"")]
    [InlineData("upper()")]
    [InlineData("contains(\"x\")")]
    [InlineData("1 and true")]
    [InlineData("\"a\" < \"b\"")]
    [InlineData("2147483648")]
    public void Evaluate_MalformedOrIllTypedInput_ReturnsMarker(string expression)
    {
        var result = ScriptExpression.Evaluate(expression, Now);

        Assert.False(result.Success);
        Assert.Matches(@"^\[script error: [a-z-]+\]$", result.Output);
    }

    [Fact]
    public void Limits_SourceTokensLiteralsDepthArgumentsAndOutput_AreEnforced()
    {
        Assert.Equal("[script error: source-limit]",
            ScriptExpression.Evaluate(new string(' ', ScriptExpression.MaxSourceChars + 1), Now).Output);

        string tooManyTokens = string.Join(' ', Enumerable.Repeat("true", ScriptExpression.MaxTokens + 1));
        Assert.Equal("[script error: token-limit]", ScriptExpression.Evaluate(tooManyTokens, Now).Output);

        string longLiteral = "\"" + new string('x', ScriptExpression.MaxLiteralChars + 1) + "\"";
        Assert.Equal("[script error: literal-limit]", ScriptExpression.Evaluate(longLiteral, Now).Output);

        string tooDeep = string.Concat(Enumerable.Repeat("not ", ScriptExpression.MaxDepth + 1)) + "true";
        Assert.Equal("[script error: depth-limit]", ScriptExpression.Evaluate(tooDeep, Now).Output);

        string tooManyArguments = "concat(" + string.Join(',', Enumerable.Repeat("\"x\"", ScriptExpression.MaxArguments + 1)) + ")";
        Assert.Equal("[script error: argument-limit]", ScriptExpression.Evaluate(tooManyArguments, Now).Output);

        string encodedExpansion = "concat(" + string.Join(',', Enumerable.Repeat("\"" + new string('<', 250) + "\"", 16)) + ")";
        Assert.True(encodedExpansion.Length <= ScriptExpression.MaxSourceChars);
        Assert.Equal("[script error: output-limit]", ScriptExpression.Evaluate(encodedExpansion, Now).Output);
    }

    [Fact]
    public void Limits_AstNodesAndEvaluationSteps_AreEnforced()
    {
        var astLimits = Limits(astNodes: 2);
        Assert.Equal("[script error: ast-limit]",
            ScriptExpression.Evaluate("true and true", Now, astLimits).Output);

        var stepLimits = Limits(evaluationSteps: 1);
        Assert.Equal("[script error: evaluation-limit]",
            ScriptExpression.Evaluate("not true", Now, stepLimits).Output);
    }

    [Fact]
    public async Task AdversarialInput_CompletesWithoutHangingOrThrowing()
    {
        string nested = new string('(', 2_000) + "true" + new string(')', 2_000);
        var task = Task.Run(() => ScriptExpression.Evaluate(nested, Now));

        var result = await task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(result.Success);
    }

    private static ScriptExpression.Limits Limits(int astNodes = ScriptExpression.MaxAstNodes,
        int evaluationSteps = ScriptExpression.MaxEvaluationSteps) => new(
        ScriptExpression.MaxSourceChars,
        ScriptExpression.MaxTokens,
        astNodes,
        ScriptExpression.MaxDepth,
        evaluationSteps,
        ScriptExpression.MaxArguments,
        ScriptExpression.MaxLiteralChars,
        ScriptExpression.MaxOutputChars);
}
