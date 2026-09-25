using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using DocuWareSageConnector.Application.Mapping;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;

namespace DocuWareSageConnector.Infrastructure.Persistence;

public sealed class SqlEntityMappingStore : IEntityMappingStore
{
    private readonly string _connectionString;

    public SqlEntityMappingStore(IConfiguration configuration)
    {
        _connectionString = ConnectorStoreConnections.RequireConnectionString(configuration);
    }

    public async Task<EntityMappingListDto> ListAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            if (!await SchemaExistsAsync(connection, cancellationToken).ConfigureAwait(false))
            {
                return new EntityMappingListDto();
            }

            return new EntityMappingListDto
            {
                SchemaReady = true,
                Mappings = await ReadAllAsync(connection, cancellationToken).ConfigureAwait(false)
            };
        }
        catch (SqlException ex)
        {
            throw new MappingStoreException(ex.Message, ex);
        }
    }

    public async Task<EntityMappingDto?> GetAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            if (!await SchemaExistsAsync(connection, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return (await ReadAllAsync(connection, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(mapping => mapping.Id == id);
        }
        catch (SqlException ex)
        {
            throw new MappingStoreException(ex.Message, ex);
        }
    }

    public async Task<EntityMappingDto> CreateAsync(
        SaveEntityMappingRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var pair = Normalize(request);
        var now = DateTime.UtcNow;
        var actor = userId.ToString("D");
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await RequireSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
            await EnsureConfigsExistAsync(connection, pair, cancellationToken).ConfigureAwait(false);
            await EnsurePairAvailableAsync(connection, pair, exceptId: null, cancellationToken).ConfigureAwait(false);
            await EnsureCodeAvailableAsync(connection, pair.Code, exceptId: null, cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(
                """
                INSERT INTO mapping_table
                    (entity_name, cabinet_name, entity_code, entity_type, cabinet_code, cabinet_type, code, description, created_by, created_at, updated_at, updated_by)
                OUTPUT INSERTED.id
                VALUES
                    (@entityName, @cabinetName, @entityCode, @entityType, @cabinetCode, @cabinetType, @code, @description, @actor, @now, @now, @actor)
                """,
                connection);
            BindPair(command, pair, actor, now);
            var id = SqlStore.InsertedId(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            return (await ReadAllAsync(connection, cancellationToken).ConfigureAwait(false))
                       .FirstOrDefault(mapping => mapping.Id == id)
                   ?? throw new MappingStoreException("The mapping was saved but could not be read back.", new InvalidOperationException());
        }
        catch (MappingConflictException)
        {
            throw;
        }
        catch (SqlException ex) when (SqlStore.IsDuplicateKey(ex))
        {
            throw new MappingConflictException("A mapping with this code, or for this entity and cabinet, already exists.");
        }
        catch (SqlException ex)
        {
            throw new MappingStoreException(ex.Message, ex);
        }
    }

    public async Task<EntityMappingDto?> UpdateAsync(
        int id,
        SaveEntityMappingRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var pair = Normalize(request);
        var now = DateTime.UtcNow;
        var actor = userId.ToString("D");
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await RequireSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
            if ((await ReadAllAsync(connection, cancellationToken).ConfigureAwait(false)).All(mapping => mapping.Id != id))
            {
                return null;
            }

            await EnsureConfigsExistAsync(connection, pair, cancellationToken).ConfigureAwait(false);
            await EnsurePairAvailableAsync(connection, pair, id, cancellationToken).ConfigureAwait(false);
            await EnsureCodeAvailableAsync(connection, pair.Code, id, cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(
                """
                UPDATE mapping_table
                SET entity_name = @entityName,
                    cabinet_name = @cabinetName,
                    entity_code = @entityCode,
                    entity_type = @entityType,
                    cabinet_code = @cabinetCode,
                    cabinet_type = @cabinetType,
                    code = @code,
                    description = @description,
                    updated_at = @now,
                    updated_by = @actor
                WHERE id = @id
                """,
                connection);
            BindPair(command, pair, actor, now);
            command.Parameters.AddWithValue("@id", id);
            var updated = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (updated == 0)
            {
                return null;
            }

            return (await ReadAllAsync(connection, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(mapping => mapping.Id == id);
        }
        catch (MappingConflictException)
        {
            throw;
        }
        catch (SqlException ex) when (SqlStore.IsDuplicateKey(ex))
        {
            throw new MappingConflictException("A mapping with this code, or for this entity and cabinet, already exists.");
        }
        catch (SqlException ex)
        {
            throw new MappingStoreException(ex.Message, ex);
        }
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            if (!await SchemaExistsAsync(connection, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            await using var command = new SqlCommand(
                "DELETE FROM mapping_table WHERE id = @id",
                connection);
            command.Parameters.AddWithValue("@id", id);
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
        }
        catch (SqlException ex)
        {
            throw new MappingStoreException(ex.Message, ex);
        }
    }

    public async Task<EntityMappingFieldDto?> AddFieldAsync(
        int mappingId,
        SaveEntityMappingFieldRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var field = NormalizeField(request);
        var now = DateTime.UtcNow;
        var actor = userId.ToString("D");
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await RequireSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
            if ((await ReadAllAsync(connection, cancellationToken).ConfigureAwait(false)).All(mapping => mapping.Id != mappingId))
            {
                return null;
            }

            await EnsureFieldAvailableAsync(connection, mappingId, field.EntityFieldName, exceptId: null, cancellationToken)
                .ConfigureAwait(false);
            await EnsureKeyOrderAvailableAsync(connection, mappingId, field, exceptId: null, cancellationToken)
                .ConfigureAwait(false);
            await using var command = new SqlCommand(
                """
                INSERT INTO mapping_field
                    (id_mapping_table, entity_field_name, cabinet_field_name, entity_type_name, cabinet_type_name,
                     entity_type_long, cabinet_type_long, is_key, key_order, created_by, created_at, updated_at, updated_by)
                OUTPUT INSERTED.id
                VALUES
                    (@mappingId, @entityFieldName, @cabinetFieldName, @entityTypeName, @cabinetTypeName,
                     @entityTypeLong, @cabinetTypeLong, @isKey, @keyOrder, @actor, @now, @now, @actor)
                """,
                connection);
            BindField(command, mappingId, field, actor, now);
            var id = SqlStore.InsertedId(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            var mapping = (await ReadAllAsync(connection, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(item => item.Id == mappingId);
            return mapping?.Fields.FirstOrDefault(item => item.Id == id);
        }
        catch (MappingConflictException)
        {
            throw;
        }
        catch (SqlException ex) when (SqlStore.IsDuplicateKey(ex))
        {
            throw new MappingConflictException("A field with this entity field name already exists on the mapping.");
        }
        catch (SqlException ex)
        {
            throw new MappingStoreException(ex.Message, ex);
        }
    }

    public async Task<EntityMappingFieldDto?> UpdateFieldAsync(
        int mappingId,
        int fieldId,
        SaveEntityMappingFieldRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var field = NormalizeField(request);
        var now = DateTime.UtcNow;
        var actor = userId.ToString("D");
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await RequireSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
            var existing = (await ReadAllAsync(connection, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(mapping => mapping.Id == mappingId)
                ?.Fields.FirstOrDefault(item => item.Id == fieldId);
            if (existing is null)
            {
                return null;
            }

            await EnsureFieldAvailableAsync(connection, mappingId, field.EntityFieldName, fieldId, cancellationToken)
                .ConfigureAwait(false);
            await EnsureKeyOrderAvailableAsync(connection, mappingId, field, fieldId, cancellationToken)
                .ConfigureAwait(false);
            await using var command = new SqlCommand(
                """
                UPDATE mapping_field
                SET entity_field_name = @entityFieldName,
                    cabinet_field_name = @cabinetFieldName,
                    entity_type_name = @entityTypeName,
                    cabinet_type_name = @cabinetTypeName,
                    entity_type_long = @entityTypeLong,
                    cabinet_type_long = @cabinetTypeLong,
                    is_key = @isKey,
                    key_order = @keyOrder,
                    updated_at = @now,
                    updated_by = @actor
                WHERE id = @id AND id_mapping_table = @mappingId
                """,
                connection);
            BindField(command, mappingId, field, actor, now);
            command.Parameters.AddWithValue("@id", fieldId);
            var updated = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (updated == 0)
            {
                return null;
            }

            var mapping = (await ReadAllAsync(connection, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(item => item.Id == mappingId);
            return mapping?.Fields.FirstOrDefault(item => item.Id == fieldId);
        }
        catch (MappingConflictException)
        {
            throw;
        }
        catch (SqlException ex) when (SqlStore.IsDuplicateKey(ex))
        {
            throw new MappingConflictException("A field with this entity field name already exists on the mapping.");
        }
        catch (SqlException ex)
        {
            throw new MappingStoreException(ex.Message, ex);
        }
    }

    public async Task<bool> DeleteFieldAsync(int mappingId, int fieldId, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            if (!await SchemaExistsAsync(connection, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            await using var command = new SqlCommand(
                """
                DELETE FROM mapping_field
                WHERE id = @id AND id_mapping_table = @mappingId
                """,
                connection);
            command.Parameters.AddWithValue("@id", fieldId);
            command.Parameters.AddWithValue("@mappingId", mappingId);
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
        }
        catch (SqlException ex)
        {
            throw new MappingStoreException(ex.Message, ex);
        }
    }

    public async Task<MappingLabels> GetLabelsAsync(CancellationToken cancellationToken)
    {
        var list = await ListAsync(cancellationToken).ConfigureAwait(false);
        return list.SchemaReady ? MappingLabels.From(list.Mappings) : MappingLabels.Empty;
    }

    private static async Task RequireSchemaAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        if (!await SchemaExistsAsync(connection, cancellationToken).ConfigureAwait(false))
        {
            throw new MappingStoreException(
                "Mapping tables mapping_table and mapping_field are not in the connector store.",
                new InvalidOperationException());
        }
    }

    private static async Task<bool> SchemaExistsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            SELECT TOP (1) COUNT(*)
            FROM information_schema.TABLES
            WHERE TABLE_CATALOG = DB_NAME()
              AND TABLE_NAME IN ('mapping_table', 'mapping_field')
            """,
            connection);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        return count == 2;
    }

    private static async Task<IReadOnlyList<EntityMappingDto>> ReadAllAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        var mappings = new List<EntityMappingDto>();
        var fields = new List<EntityMappingFieldDto>();
        await using var command = new SqlCommand(
            """
            SELECT id, entity_name, cabinet_name, entity_code, entity_type, cabinet_code, cabinet_type, code, description
            FROM mapping_table
            ORDER BY code, id;

            SELECT id, id_mapping_table, entity_field_name, cabinet_field_name,
                   entity_type_name, cabinet_type_name, entity_type_long, cabinet_type_long,
                   is_key, key_order
            FROM mapping_field
            ORDER BY id_mapping_table,
                     CASE WHEN is_key = 1 THEN 0 ELSE 1 END,
                     key_order,
                     entity_field_name,
                     id
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            mappings.Add(new EntityMappingDto
            {
                Id = reader.GetInt32("id"),
                EntityName = reader.GetString("entity_name"),
                CabinetName = reader.GetString("cabinet_name"),
                EntityConfigCode = reader.GetString("entity_code"),
                EntityConfigType = reader.GetString("entity_type"),
                CabinetConfigCode = reader.GetString("cabinet_code"),
                CabinetConfigType = reader.GetString("cabinet_type"),
                Code = reader.GetString("code"),
                Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString("description")
            });
        }

        await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            fields.Add(new EntityMappingFieldDto
            {
                Id = reader.GetInt32("id"),
                MappingId = reader.GetInt32("id_mapping_table"),
                EntityFieldName = reader.GetString("entity_field_name"),
                CabinetFieldName = reader.GetString("cabinet_field_name"),
                EntityTypeName = reader.IsDBNull(reader.GetOrdinal("entity_type_name")) ? null : reader.GetString("entity_type_name"),
                CabinetTypeName = reader.IsDBNull(reader.GetOrdinal("cabinet_type_name")) ? null : reader.GetString("cabinet_type_name"),
                EntityTypeLong = reader.IsDBNull(reader.GetOrdinal("entity_type_long")) ? null : reader.GetInt32("entity_type_long"),
                CabinetTypeLong = reader.IsDBNull(reader.GetOrdinal("cabinet_type_long")) ? null : reader.GetInt32("cabinet_type_long"),
                IsKey = !reader.IsDBNull(reader.GetOrdinal("is_key")) && reader.GetBoolean("is_key"),
                KeyOrder = reader.IsDBNull(reader.GetOrdinal("key_order")) ? null : reader.GetInt32("key_order")
            });
        }

            return mappings
            .Select(mapping => new EntityMappingDto
            {
                Id = mapping.Id,
                EntityName = mapping.EntityName,
                CabinetName = mapping.CabinetName,
                EntityConfigCode = mapping.EntityConfigCode,
                EntityConfigType = mapping.EntityConfigType,
                CabinetConfigCode = mapping.CabinetConfigCode,
                CabinetConfigType = mapping.CabinetConfigType,
                Code = mapping.Code,
                Description = mapping.Description,
                Fields = fields.Where(field => field.MappingId == mapping.Id).ToArray()
            })
            .ToArray();
    }

    private static async Task EnsurePairAvailableAsync(
        SqlConnection connection,
        NormalizedMapping pair,
        int? exceptId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            SELECT id
            FROM mapping_table
            WHERE entity_name = @entityName
              AND cabinet_name = @cabinetName
              AND entity_code = @entityCode
              AND entity_type = @entityType
              AND cabinet_code = @cabinetCode
              AND cabinet_type = @cabinetType
              AND (@exceptId IS NULL OR id <> @exceptId)
            """,
            connection);
        BindPair(command, pair, actor: string.Empty, now: DateTime.UtcNow);
        command.Parameters.AddWithValue("@exceptId", exceptId.HasValue ? exceptId.Value : DBNull.Value);
        var existing = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null and not DBNull)
        {
            throw new MappingConflictException("A mapping for this entity and cabinet already exists.");
        }
    }

    private static async Task EnsureCodeAvailableAsync(
        SqlConnection connection,
        string code,
        int? exceptId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            SELECT id
            FROM mapping_table
            WHERE code = @code
              AND (@exceptId IS NULL OR id <> @exceptId)
            """,
            connection);
        command.Parameters.AddWithValue("@code", code);
        command.Parameters.AddWithValue("@exceptId", exceptId.HasValue ? exceptId.Value : DBNull.Value);
        var existing = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null and not DBNull)
        {
            throw new MappingConflictException("A mapping with this code already exists.");
        }
    }

    private static async Task EnsureFieldAvailableAsync(
        SqlConnection connection,
        int mappingId,
        string entityFieldName,
        int? exceptId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            SELECT TOP (1) id
            FROM mapping_field
            WHERE id_mapping_table = @mappingId
              AND entity_field_name = @entityFieldName
              AND (@exceptId IS NULL OR id <> @exceptId)
            """,
            connection);
        command.Parameters.AddWithValue("@mappingId", mappingId);
        command.Parameters.AddWithValue("@entityFieldName", entityFieldName);
        command.Parameters.AddWithValue("@exceptId", exceptId.HasValue ? exceptId.Value : DBNull.Value);
        var existing = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null and not DBNull)
        {
            throw new MappingConflictException("A field with this entity field name already exists on the mapping.");
        }
    }

    private static async Task EnsureKeyOrderAvailableAsync(
        SqlConnection connection,
        int mappingId,
        NormalizedMappingField field,
        int? exceptId,
        CancellationToken cancellationToken)
    {
        if (!field.IsKey || field.KeyOrder is null)
        {
            return;
        }

        await using var command = new SqlCommand(
            """
            SELECT TOP (1) id
            FROM mapping_field
            WHERE id_mapping_table = @mappingId
              AND is_key = 1
              AND key_order = @keyOrder
              AND (@exceptId IS NULL OR id <> @exceptId)
            """,
            connection);
        command.Parameters.AddWithValue("@mappingId", mappingId);
        command.Parameters.AddWithValue("@keyOrder", field.KeyOrder.Value);
        command.Parameters.AddWithValue("@exceptId", exceptId.HasValue ? exceptId.Value : DBNull.Value);
        var existing = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null and not DBNull)
        {
            throw new MappingConflictException("Another key field already uses this key order.");
        }
    }

    private static void BindPair(SqlCommand command, NormalizedMapping pair, string actor, DateTime now)
    {
        command.Parameters.AddWithValue("@entityName", pair.EntityName);
        command.Parameters.AddWithValue("@cabinetName", pair.CabinetName);
        command.Parameters.AddWithValue("@entityCode", pair.EntityConfig.Code);
        command.Parameters.AddWithValue("@entityType", pair.EntityConfig.Type);
        command.Parameters.AddWithValue("@cabinetCode", pair.CabinetConfig.Code);
        command.Parameters.AddWithValue("@cabinetType", pair.CabinetConfig.Type);
        command.Parameters.AddWithValue("@code", pair.Code);
        command.Parameters.AddWithValue("@description", (object?)pair.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("@actor", actor);
        command.Parameters.AddWithValue("@now", now);
    }

    private static NormalizedMappingField NormalizeField(SaveEntityMappingFieldRequest request) =>
        EntityMappingRules.NormalizeField(
            request.EntityFieldName,
            request.CabinetFieldName,
            request.EntityTypeName,
            request.CabinetTypeName,
            request.EntityTypeLong,
            request.CabinetTypeLong,
            request.IsKey,
            request.KeyOrder);

    private static NormalizedMapping Normalize(SaveEntityMappingRequest request) =>
        EntityMappingRules.NormalizePair(
            request.EntityName,
            request.CabinetName,
            request.EntityConfigCode,
            request.EntityConfigType,
            request.CabinetConfigCode,
            request.CabinetConfigType,
            request.Code,
            request.Description);

    private static async Task EnsureConfigsExistAsync(
        SqlConnection connection,
        NormalizedMapping pair,
        CancellationToken cancellationToken)
    {
        await EnsureConfigExistsAsync(connection, pair.EntityConfig, "Sage configuration", cancellationToken).ConfigureAwait(false);
        await EnsureConfigExistsAsync(connection, pair.CabinetConfig, "DocuWare configuration", cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureConfigExistsAsync(
        SqlConnection connection,
        MappingConfigRef config,
        string label,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            SELECT COUNT(*)
            FROM (
                SELECT DISTINCT code, type
                FROM setting
                WHERE type = @type AND code = @code
            ) AS configuration
            """,
            connection);
        command.Parameters.AddWithValue("@type", config.Type);
        command.Parameters.AddWithValue("@code", config.Code);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        if (count == 0)
        {
            throw new ArgumentException($"{label} {config.Code} ({config.Type}) was not found.");
        }
    }

    private static void BindField(
        SqlCommand command,
        int mappingId,
        NormalizedMappingField field,
        string actor,
        DateTime now)
    {
        command.Parameters.AddWithValue("@mappingId", mappingId);
        command.Parameters.AddWithValue("@entityFieldName", field.EntityFieldName);
        command.Parameters.AddWithValue("@cabinetFieldName", field.CabinetFieldName);
        command.Parameters.AddWithValue("@entityTypeName", (object?)field.EntityTypeName ?? DBNull.Value);
        command.Parameters.AddWithValue("@cabinetTypeName", (object?)field.CabinetTypeName ?? DBNull.Value);
        command.Parameters.AddWithValue("@entityTypeLong", (object?)field.EntityTypeLong ?? DBNull.Value);
        command.Parameters.AddWithValue("@cabinetTypeLong", (object?)field.CabinetTypeLong ?? DBNull.Value);
        command.Parameters.AddWithValue("@isKey", field.IsKey);
        command.Parameters.AddWithValue("@keyOrder", (object?)field.KeyOrder ?? DBNull.Value);
        command.Parameters.AddWithValue("@actor", actor);
        command.Parameters.AddWithValue("@now", now);
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
