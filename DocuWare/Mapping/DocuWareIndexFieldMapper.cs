using DocuWare.Platform.ServerClient;
using DocuWareSageConnector.Domain.Entities;
using DocuWareSageConnector.Domain.Enums;
using DocuWareSageConnector.Domain.Mapping;

namespace DocuWareSageConnector.DocuWare.Mapping;

public static class DocuWareIndexFieldMapper
{
    public static DocumentIndexField ToSdkField(IndexFieldValue field)
    {
        return field.Type switch
        {
            IndexFieldType.Numeric when field.Value is not null =>
                DocumentIndexField.Create(field.Name, Convert.ToDecimal(field.Value)),
            IndexFieldType.DateTime when ValueConverters.ToDateTime(field.Value) is { } date =>
                DocumentIndexField.Create(field.Name, date),
            _ => DocumentIndexField.Create(field.Name, ValueConverters.ToText(field.Value) ?? string.Empty)
        };
    }

    public static IReadOnlyList<IndexFieldValue> FromDocument(Document document, EntityType entityType)
    {
        var result = new List<IndexFieldValue>();
        foreach (var mapping in EntityFieldMaps.For(entityType))
        {
            var sdkField = TryGetField(document, mapping.DocuWareField);
            if (sdkField is null || sdkField.IsNull || sdkField.Item is null)
            {
                continue;
            }

            var formatted = ValueConverters.FormatForDocuWare(sdkField.Item, mapping.Type);
            if (formatted is null)
            {
                continue;
            }

            result.Add(new IndexFieldValue
            {
                Name = mapping.DocuWareField,
                Type = mapping.Type,
                Value = formatted
            });
        }

        return result;
    }

    public static DocuWareDocumentInfo ToInfo(Document document, EntityType entityType) =>
        new()
        {
            Id = document.Id,
            Title = document.Title,
            Fields = FromDocument(document, entityType)
        };

    private static DocumentIndexField? TryGetField(Document document, string fieldName)
    {
        try
        {
            return document[fieldName];
        }
        catch (Exception)
        {
            return document.Fields?.FirstOrDefault(field =>
                string.Equals(field.FieldName, fieldName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
