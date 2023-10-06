using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PersistentJobs;

internal static class SqliteJsonDocument {
    public static JsonDocument Parser(string? input)
    {
        if (input is null || input == "")
        {
            return JsonDocument.Parse("{}");
        }

        return JsonDocument.Parse(input, new JsonDocumentOptions());
    }

    public static string Serializer(JsonDocument? input)
    {
        if (input is null)
        {
            return "{}";
        }
        // Convert JsonDocument to JSON string
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        input.WriteTo(writer);
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());        
    }
}

// Extension class for Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<JsonDocument?>

public static class SqliteJsonDocumentPropertyBuilderExtensions
{
    public static PropertyBuilder<JsonDocument?> HasSqliteJsonDocumentConversion(this PropertyBuilder<JsonDocument?> propertyBuilder)
    {
        propertyBuilder.HasConversion(
            v => SqliteJsonDocument.Serializer(v),
            v => SqliteJsonDocument.Parser(v)
        );
        return propertyBuilder;
    }
}