using System.ComponentModel;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using XafMcp.Module.BusinessObjects;
using XafMcp.Module.Schema;

namespace XafMcp.Blazor.Server.Mcp;

[McpServerToolType]
public sealed class SchemaTools(IConfiguration configuration) {

    [McpServerTool(Name = "check_schema_drift")]
    [Description("Compare the EF Core model against the live database schema (INFORMATION_SCHEMA). Reports extra/missing tables and columns, type, length, precision and nullability mismatches.")]
    public string CheckSchemaDrift() {
        var connectionString = configuration.GetConnectionString("ConnectionString")
            ?? throw new ModelContextProtocol.McpException("No connection string configured");
        var findings = SchemaDriftComparer.Compare(ReadModelColumns(connectionString), ReadDatabaseColumns(connectionString));
        return JsonSerializer.Serialize(new {
            status = findings.Count == 0 ? "in_sync" : "drift_detected",
            findingCount = findings.Count,
            findings,
        }, JsonOpts.Indented);
    }

    static List<ColumnInfo> ReadModelColumns(string connectionString) {
        // Schema work is deliberately outside XAF/security: a plain DbContext over the same model.
        var options = new DbContextOptionsBuilder<XafMcpEFCoreDbContext>()
            .UseSqlServer(connectionString)
            .UseChangeTrackingProxies() // same as ModelSmokeTests: notification strategy fails validation without proxies
            .Options;
        using var ctx = new XafMcpEFCoreDbContext(options);
        var result = new List<ColumnInfo>();
        foreach (var table in ctx.Model.GetRelationalModel().Tables) {
            foreach (var column in table.Columns) {
                result.Add(new ColumnInfo(table.Name, column.Name, column.StoreType, column.IsNullable));
            }
        }
        return result;
    }

    static List<ColumnInfo> ReadDatabaseColumns(string connectionString) {
        var result = new List<ColumnInfo>();
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 30;
        command.CommandText = """
            SELECT c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH,
                   c.NUMERIC_PRECISION, c.NUMERIC_SCALE, c.DATETIME_PRECISION, c.IS_NULLABLE
            FROM INFORMATION_SCHEMA.COLUMNS c
            JOIN INFORMATION_SCHEMA.TABLES t
              ON t.TABLE_NAME = c.TABLE_NAME AND t.TABLE_SCHEMA = c.TABLE_SCHEMA
            WHERE t.TABLE_TYPE = 'BASE TABLE' AND c.TABLE_SCHEMA = 'dbo'
            ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read()) {
            var dataType = reader.GetString(2);
            var maxLength = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3);
            var precision = reader.IsDBNull(4) ? (byte?)null : reader.GetByte(4);
            var scale = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5);
            var datetimePrecision = reader.IsDBNull(6) ? (short?)null : reader.GetInt16(6);
            result.Add(new ColumnInfo(
                reader.GetString(0), reader.GetString(1),
                ComposeStoreType(dataType, maxLength, precision, scale, datetimePrecision),
                reader.GetString(7) == "YES"));
        }
        return result;
    }

    // Mirrors EF SqlServer store-type strings so the pure comparer sees the same vocabulary.
    static string ComposeStoreType(string dataType, int? maxLength, byte? precision, int? scale, short? datetimePrecision) =>
        dataType switch {
            "nvarchar" or "varchar" or "nchar" or "char" or "varbinary" or "binary" =>
                $"{dataType}({(maxLength == -1 ? "max" : maxLength?.ToString() ?? "max")})",
            "decimal" or "numeric" => $"decimal({precision},{scale})",
            "datetime2" or "time" or "datetimeoffset" when datetimePrecision.HasValue && datetimePrecision != 7 =>
                $"{dataType}({datetimePrecision})",
            _ => dataType,
        };
}
