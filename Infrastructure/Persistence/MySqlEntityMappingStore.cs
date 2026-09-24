using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using DocuWareSageConnector.Application.Mapping;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace DocuWareSageConnector.Infrastructure.Persistence;

public sealed class MySqlEntityMappingStore : IEntityMappingStore
{
    private readonly string _connectionString;

    public MySqlEntityMappingStore(IConfiguration configuration)
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
        catch (MySqlException ex)
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
        catch (MySqlException ex)
        {
            throw new MappingStoreException(ex.Message, ex);
        }
    }

    public async Task<EntityMappingDto> CreateAsync(
        SaveEntityMappingRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var pair = EntityMappingRules.NormalizePair(request.EntityName, request.CabinetName);
        var now = DateTime.UtcNow;
        var actor = userId.ToString("D");
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await RequireSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
            await EnsurePairAvailableAsync(connection, pair, exceptId: null, cancellationToken).ConfigureAwait(false);
            await using var command = new MySqlCommand(
                """
                INSERT INTO mapping_table
                    (entity_name, cabinet_name, created_by, created_at, updated_at, updated_by)
                VALUES
                    (@entityName, @cabinetName, @actor, @now, @now, @actor)
                """,
                connection);
            BindPair(command, pair, actor, now);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var id = Convert.ToInt32(command.LastInsertedId);
            return (await ReadAllAsync(connection, cancellationToken).ConfigureAwait(false))
                       .FirstOrDefault(mapping => mapping.Id == id)
                   ?? throw new MappingStoreException("The mapping was saved but could not be read back.", new InvalidOperationException());
        }
        catch (MappingConflictException)
        {
            throw;
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            throw new MappingConflictException("A mapping for this entity and cabinet already exists.");
        }
        catch (MySqlException ex)
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
        var pair = EntityMappingRules.NormalizePair(request.EntityName, request.CabinetName);
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

            await EnsurePairAvailableAsync(connection, pair, id, cancellationToken).ConfigureAwait(false);
            await using var command = new MySqlCommand(
                """
                UPDATE mapping_table
                SET entity_name = @entityName,
                    cabinet_name = @cabinetName,
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
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            throw new MappingConflictException("A mapping for this entity and cabinet already exists.");
        }
        catch (MySqlException ex)
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

            await using var command = new MySqlCommand(
                "DELETE FROM mapping_table WHERE id = @id",
                connection);
            command.Parameters.AddWithValue("@id", id);
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
        }
        catch (MySqlException ex)
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
        var field = EntityMappingRules.NormalizeField(
            request.EntityFieldName,
            request.CabinetFieldName,
            request.EntityTypeName,
            request.CabinetTypeName,
            request.EntityTypeLong,
            request.CabinetTypeLong);
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
            await using var command = new MySqlCommand(
                """
                INSERT INTO mapping_field
                    (id_mapping_table, entity_field_name, cabinet_field_name, entity_type_name, cabinet_type_name,
                     entity_type_long, cabinet_type_long, created_by, created_at, updated_at, updated_by)
                VALUES
                    (@mappingId, @entityFieldName, @cabinetFieldName, @entityTypeName, @cabinetTypeName,
                     @entityTypeLong, @cabinetTypeLong, @actor, @now, @now, @actor)
                """,
                connection);
            BindField(command, mappingId, field, actor, now);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var id = Convert.ToInt32(command.LastInsertedId);
            var mapping = (await ReadAllAsync(connection, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(item => item.Id == mappingId);
            return mapping?.Fields.FirstOrDefault(item => item.Id == id);
        }
        catch (MappingConflictException)
        {
            throw;
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            throw new MappingConflictException("A field with this entity field name already exists on the mapping.");
        }
        catch (MySqlException ex)
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

            await using var command = new MySqlCommand(
                """
                DELETE FROM mapping_field
                WHERE id = @id AND id_mapping_table = @mappingId
                """,
                connection);
            command.Parameters.AddWithValue("@id", fieldId);
            command.Parameters.AddWithValue("@mappingId", mappingId);
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
        }
        catch (MySqlException ex)
        {
            throw new MappingStoreException(ex.Message, ex);
        }
    }

    public async Task<MappingLabels> GetLabelsAsync(CancellationToken cancellationToken)
    {
        var list = await ListAsync(cancellationToken).ConfigureAwait(false);
        return list.SchemaReady ? MappingLabels.From(list.Mappings) : MappingLabels.Empty;
    }

    private static async Task RequireSchemaAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        if (!await SchemaExistsAsync(connection, cancellationToken).ConfigureAwait(false))
        {
            throw new MappingStoreException(
                "Mapping tables mapping_table and mapping_field are not in the connector store.",
                new InvalidOperationException());
        }
    }

    private static async Task<bool> SchemaExistsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(
            """
            SELECT COUNT(*)
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME IN ('mapping_table', 'mapping_field')
            """,
            connection);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        return count == 2;
    }

    private static async Task<IReadOnlyList<EntityMappingDto>> ReadAllAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        var mappings = new List<EntityMappingDto>();
        var fields = new List<EntityMappingFieldDto>();
        await using var command = new MySqlCommand(
            """
            SELECT id, entity_name, cabinet_name
            FROM mapping_table
            ORDER BY entity_name, cabinet_name, id;

            SELECT id, id_mapping_table, entity_field_name, cabinet_field_name,
                   entity_type_name, cabinet_type_name, entity_type_long, cabinet_type_long
            FROM mapping_field
            ORDER BY id_mapping_table, entity_field_name, id
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            mappings.Add(new EntityMappingDto
            {
                Id = reader.GetInt32("id"),
                EntityName = reader.GetString("entity_name"),
                CabinetName = reader.GetString("cabinet_name")
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
                CabinetTypeLong = reader.IsDBNull(reader.GetOrdinal("cabinet_type_long")) ? null : reader.GetInt32("cabinet_type_long")
            });
        }

        return mappings
            .Select(mapping => new EntityMappingDto
            {
                Id = mapping.Id,
                EntityName = mapping.EntityName,
                CabinetName = mapping.CabinetName,
                Fields = fields.Where(field => field.MappingId == mapping.Id).ToArray()
            })
            .ToArray();
    }

    private static async Task EnsurePairAvailableAsync(
        MySqlConnection connection,
        NormalizedMapping pair,
        int? exceptId,
        CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(
            """
            SELECT id
            FROM mapping_table
            WHERE entity_name = @entityName
              AND cabinet_name = @cabinetName
              AND (@exceptId IS NULL OR id <> @exceptId)
            LIMIT 1
            """,
            connection);
        command.Parameters.AddWithValue("@entityName", pair.EntityName);
        command.Parameters.AddWithValue("@cabinetName", pair.CabinetName);
        command.Parameters.AddWithValue("@exceptId", exceptId.HasValue ? exceptId.Value : DBNull.Value);
        var existing = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null and not DBNull)
        {
            throw new MappingConflictException("A mapping for this entity and cabinet already exists.");
        }
    }

    private static async Task EnsureFieldAvailableAsync(
        MySqlConnection connection,
        int mappingId,
        string entityFieldName,
        int? exceptId,
        CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(
            """
            SELECT id
            FROM mapping_field
            WHERE id_mapping_table = @mappingId
              AND entity_field_name = @entityFieldName
              AND (@exceptId IS NULL OR id <> @exceptId)
            LIMIT 1
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

    private static void BindPair(MySqlCommand command, NormalizedMapping pair, string actor, DateTime now)
    {
        command.Parameters.AddWithValue("@entityName", pair.EntityName);
        command.Parameters.AddWithValue("@cabinetName", pair.CabinetName);
        command.Parameters.AddWithValue("@actor", actor);
        command.Parameters.AddWithValue("@now", now);
    }

    private static void BindField(
        MySqlCommand command,
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
        command.Parameters.AddWithValue("@actor", actor);
        command.Parameters.AddWithValue("@now", now);
    }

    private async Task<MySqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
