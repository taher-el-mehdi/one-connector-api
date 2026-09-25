using System.Text;
using DocuWareSageConnector.Application.DTOs;
using Microsoft.Data.SqlClient;

namespace DocuWareSageConnector.Application.Synchronization;

public static class SynchronizationSourceQuery
{
    public static (string WhereSql, IReadOnlyList<SqlParameter> Parameters) Where(
        IReadOnlyList<SynchronizationFilterDto> filters)
    {
        if (filters.Count == 0)
        {
            return (string.Empty, []);
        }

        var sql = new StringBuilder();
        var parameters = new List<SqlParameter>();
        for (var index = 0; index < filters.Count; index++)
        {
            var filter = filters[index];
            var column = Bracket(filter.FieldName);
            if (index > 0)
            {
                var logical = string.Equals(filter.LogicalOperator, "OR", StringComparison.OrdinalIgnoreCase) ? "OR" : "AND";
                sql.Append(' ').Append(logical).Append(' ');
            }

            var parameterName = "@filter" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            switch (filter.Operator)
            {
                case "is_null":
                    sql.Append(column).Append(" IS NULL");
                    break;
                case "not_null":
                    sql.Append(column).Append(" IS NOT NULL");
                    break;
                case "equals":
                    sql.Append(column).Append(" = ").Append(parameterName);
                    parameters.Add(new SqlParameter(parameterName, filter.Value ?? string.Empty));
                    break;
                case "not_equals":
                    sql.Append(column).Append(" <> ").Append(parameterName);
                    parameters.Add(new SqlParameter(parameterName, filter.Value ?? string.Empty));
                    break;
                case "contains":
                    sql.Append(column).Append(" LIKE ").Append(parameterName);
                    parameters.Add(new SqlParameter(parameterName, "%" + EscapeLike(filter.Value) + "%"));
                    break;
                case "starts_with":
                    sql.Append(column).Append(" LIKE ").Append(parameterName);
                    parameters.Add(new SqlParameter(parameterName, EscapeLike(filter.Value) + "%"));
                    break;
                case "ends_with":
                    sql.Append(column).Append(" LIKE ").Append(parameterName);
                    parameters.Add(new SqlParameter(parameterName, "%" + EscapeLike(filter.Value)));
                    break;
                case "gt":
                    sql.Append(column).Append(" > ").Append(parameterName);
                    parameters.Add(new SqlParameter(parameterName, filter.Value ?? string.Empty));
                    break;
                case "gte":
                    sql.Append(column).Append(" >= ").Append(parameterName);
                    parameters.Add(new SqlParameter(parameterName, filter.Value ?? string.Empty));
                    break;
                case "lt":
                    sql.Append(column).Append(" < ").Append(parameterName);
                    parameters.Add(new SqlParameter(parameterName, filter.Value ?? string.Empty));
                    break;
                case "lte":
                    sql.Append(column).Append(" <= ").Append(parameterName);
                    parameters.Add(new SqlParameter(parameterName, filter.Value ?? string.Empty));
                    break;
                default:
                    throw new InvalidOperationException($"Filter operator '{filter.Operator}' is not supported.");
            }
        }

        return (sql.ToString(), parameters);
    }

    private static string Bracket(string fieldName)
    {
        var name = fieldName.Trim();
        if (name.Length == 0 || name.Contains(']') || name.Contains('['))
        {
            throw new InvalidOperationException($"Filter field '{fieldName}' is not a valid column.");
        }

        return "[" + name + "]";
    }

    private static string EscapeLike(string? value)
    {
        return (value ?? string.Empty)
            .Replace("[", "[[]", StringComparison.Ordinal)
            .Replace("%", "[%]", StringComparison.Ordinal)
            .Replace("_", "[_]", StringComparison.Ordinal);
    }
}
