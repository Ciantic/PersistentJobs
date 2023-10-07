using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PersistentJobs;

internal static class JsonNodeParsing {
    public static JsonNode Parser(string? input)
    {
        if (input is null || input == "")
        {
            return new JsonObject();
        }

        var value = JsonNode.Parse(input);
        if (value is null)
        {
            return new JsonObject();
        }
        return value;
    }

    public static string Serializer(JsonNode? input)
    {
        if (input is null)
        {
            return "{}";
        }
        return input.ToJsonString();
    }
}

public static class JsonNodePropertyBuilderExtensions
{
    public static PropertyBuilder<JsonNode?> HasJsonNodeConversion(this PropertyBuilder<JsonNode?> propertyBuilder)
    {
        propertyBuilder.HasConversion(
            v => JsonNodeParsing.Serializer(v),
            v => JsonNodeParsing.Parser(v)
        );
        return propertyBuilder;
    }
}