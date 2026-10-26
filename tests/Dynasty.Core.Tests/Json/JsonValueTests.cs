using Dynasty.Core.Json;
using Dynasty.Core.Mathematics;

namespace Dynasty.Core.Tests.Json;

/// <summary>
/// The JSON reader used for ruleset packs: no external dependencies, member order kept, numbers read
/// exactly from their decimal text (no floating point in the simulation), malformed input rejected
/// with its position.
/// </summary>
public class JsonValueTests
{
    [Fact]
    public void Parse_ReadsObjectsArraysAndPrimitives()
    {
        JsonValue doc = JsonValue.Parse("{ \"name\": \"Anti-Mage\", \"roles\": [\"Carry\", \"Escape\"], \"melee\": true, \"talent\": null, \"range\": 150 }");

        Assert.Equal(JsonKind.Object, doc.Kind);
        Assert.Equal("Anti-Mage", doc["name"].AsString());
        Assert.Equal(new[] { "Carry", "Escape" }, doc["roles"].Items.Select(i => i.AsString()));
        Assert.True(doc["melee"].AsBool());
        Assert.True(doc["talent"].IsNull);
        Assert.Equal(150, doc["range"].AsInt());
        Assert.Equal(new[] { "name", "roles", "melee", "talent", "range" }, doc.Members.Select(m => m.Key));
    }

    [Theory]
    [InlineData("1.6", 16, 10)]
    [InlineData("-0.25", -1, 4)]
    [InlineData("2", 2, 1)]
    [InlineData("0.05", 1, 20)]
    [InlineData("1e2", 100, 1)]
    public void Numbers_AreReadExactlyAsFixed(string text, long numerator, long denominator)
    {
        Assert.Equal(Fixed.FromRatio(numerator, denominator), JsonValue.Parse(text).AsFixed());
    }

    [Fact]
    public void Strings_UnescapeSequences()
    {
        Assert.Equal("a\"b\\c\ndé", JsonValue.Parse("\"a\\\"b\\\\c\\nd\\u00e9\"").AsString());
    }

    [Fact]
    public void MissingMember_IsReportedByName()
    {
        JsonValue doc = JsonValue.Parse("{\"a\": 1}");

        Assert.False(doc.Has("b"));
        Assert.Null(doc.Get("b"));
        var error = Assert.Throws<JsonException>(() => doc["b"]);
        Assert.Contains("\"b\"", error.Message);
    }

    [Theory]
    [InlineData("{\"a\": 1,}")]
    [InlineData("[1 2]")]
    [InlineData("{\"a\": 1, \"a\": 2}")]
    [InlineData("\"open")]
    [InlineData("tru")]
    [InlineData("{} extra")]
    public void MalformedInput_IsRejectedWithItsPosition(string text)
    {
        var error = Assert.Throws<JsonException>(() => JsonValue.Parse(text));
        Assert.Contains("position", error.Message);
    }

    [Fact]
    public void DeepNesting_IsRejected()
    {
        string deep = new string('[', 200) + new string(']', 200);

        Assert.Throws<JsonException>(() => JsonValue.Parse(deep));
    }

    [Fact]
    public void WrongType_IsAnError()
    {
        Assert.Throws<JsonException>(() => JsonValue.Parse("\"x\"").AsInt());
        Assert.Throws<JsonException>(() => JsonValue.Parse("1.5").AsInt());
    }
}
