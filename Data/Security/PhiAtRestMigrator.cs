using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace MedicalManager.Data.Security;

/// <summary>
/// Rewrites plaintext PHI and account identifiers already stored in the database.
/// Safe to run on every startup: values that are already ciphertext are left alone.
/// </summary>
public static class PhiAtRestMigrator
{
    private static readonly (string Table, string Column, bool Deterministic)[] TextColumns =
    [
        ("AspNetUsers", "FullName", false),
        ("AspNetUsers", "PhoneNumber", false),
        ("PatientProfiles", "Sex", false),
        ("PatientProfiles", "Phone", false),
        ("PatientProfiles", "HomeAddress", false),
        ("PatientProfiles", "NamePronunciation", false),
        ("PatientProfiles", "Notes", false),
        ("PatientProfiles", "FamilyContactName", false),
        ("PatientProfiles", "FamilyContactPhone", false),
        ("PatientProfiles", "DateOfBirth", false),
        ("BloodPressureReadings", "Systolic", false),
        ("BloodPressureReadings", "Diastolic", false),
        ("BloodPressureReadings", "Pulse", false),
        ("BloodPressureReadings", "Notes", false),
        ("SugarReadings", "Value", false),
        ("SugarReadings", "Unit", false),
        ("SugarReadings", "Kind", false),
        ("SugarReadings", "Notes", false),
        ("Medications", "Name", false),
        ("Medications", "Dosage", false),
        ("Medications", "Frequency", false),
        ("Medications", "Purpose", false),
        ("Medications", "Notes", false),
        ("Medications", "PrescribedBy", false),
        ("Appointments", "Title", false),
        ("Appointments", "ProviderName", false),
        ("Appointments", "Location", false),
        ("Appointments", "Purpose", false),
        ("Appointments", "Notes", false),
        ("Doctors", "Name", false),
        ("Doctors", "Specialty", false),
        ("Doctors", "PhoneNumber", false),
        ("LabReports", "Notes", false),
        ("LabResults", "Value", false),
        ("LabResults", "Unit", false),
        ("ToDoItems", "Description", false),
        ("ToDoItems", "Notes", false),
        ("MedicationAdministrations", "RecordedByName", false),
        ("AppointmentReminders", "Detail", false)
    ];

    public static async Task<int> ApplyAsync(ApplicationDbContext db, IPhiProtector protector)
    {
        await WidenStoredColumnsAsync(db);

        var connection = db.Database.GetDbConnection();
        var wasOpen = connection.State == ConnectionState.Open;
        if (!wasOpen)
            await connection.OpenAsync();

        try
        {
            var updated = 0;
            updated += await EncryptIdentityAsync(connection, protector);
            foreach (var (table, column, _) in TextColumns)
                updated += await EncryptTextAsync(connection, protector, table, column, deterministic: false);
            updated += await EncryptTextAsync(connection, protector, "LabResults", "TestCode", deterministic: true);
            updated += await EncryptPhotoAsync(connection, protector, "ProfilePhotoData");
            updated += await EncryptPhotoAsync(connection, protector, "FamilyContactPhotoData");
            return updated;
        }
        finally
        {
            if (!wasOpen)
                await connection.CloseAsync();
        }
    }

    private static async Task WidenStoredColumnsAsync(ApplicationDbContext db)
    {
        if (db.Database.IsSqlite())
            return;

        string[] statements =
        [
            """ALTER TABLE "AspNetUsers" ALTER COLUMN "Email" TYPE text""",
            """ALTER TABLE "AspNetUsers" ALTER COLUMN "NormalizedEmail" TYPE text""",
            """ALTER TABLE "AspNetUsers" ALTER COLUMN "UserName" TYPE text""",
            """ALTER TABLE "AspNetUsers" ALTER COLUMN "NormalizedUserName" TYPE text""",
            """ALTER TABLE "AspNetUsers" ALTER COLUMN "PhoneNumber" TYPE text""",
            """ALTER TABLE "AspNetUsers" ALTER COLUMN "FullName" TYPE text""",
            """ALTER TABLE "BloodPressureReadings" ALTER COLUMN "Systolic" TYPE text USING "Systolic"::text""",
            """ALTER TABLE "BloodPressureReadings" ALTER COLUMN "Diastolic" TYPE text USING "Diastolic"::text""",
            """ALTER TABLE "BloodPressureReadings" ALTER COLUMN "Pulse" TYPE text USING "Pulse"::text""",
            """ALTER TABLE "SugarReadings" ALTER COLUMN "Value" TYPE text USING "Value"::text""",
            """ALTER TABLE "SugarReadings" ALTER COLUMN "Unit" TYPE text""",
            """ALTER TABLE "SugarReadings" ALTER COLUMN "Kind" TYPE text USING "Kind"::text""",
            """ALTER TABLE "LabResults" ALTER COLUMN "TestCode" TYPE text""",
            """ALTER TABLE "LabResults" ALTER COLUMN "Value" TYPE text USING "Value"::text""",
            """ALTER TABLE "LabResults" ALTER COLUMN "Unit" TYPE text""",
            """ALTER TABLE "AppointmentReminders" ALTER COLUMN "Detail" TYPE text"""
        ];

        foreach (var statement in statements)
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(statement);
            }
            catch (Exception)
            {
                // The table or column is not present yet, or it is already text.
            }
        }
    }

    private static async Task<int> EncryptIdentityAsync(System.Data.Common.DbConnection connection, IPhiProtector protector)
    {
        var rows = new List<(string Id, string? Email, string? NormalizedEmail, string? UserName, string? NormalizedUserName)>();
        await using (var select = connection.CreateCommand())
        {
            select.CommandText = """
                SELECT "Id", "Email", "NormalizedEmail", "UserName", "NormalizedUserName"
                FROM "AspNetUsers"
                """;
            try
            {
                await using var reader = await select.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    rows.Add((
                        reader.GetString(0),
                        ReadText(reader, 1),
                        ReadText(reader, 2),
                        ReadText(reader, 3),
                        ReadText(reader, 4)));
                }
            }
            catch (Exception)
            {
                return 0;
            }
        }

        var updated = 0;
        foreach (var row in rows)
        {
            var email = SealLookup(protector, row.Email);
            var normalizedEmail = SealLookup(protector, row.NormalizedEmail);
            var userName = SealLookup(protector, row.UserName);
            var normalizedUserName = SealLookup(protector, row.NormalizedUserName);
            if (email == row.Email
                && normalizedEmail == row.NormalizedEmail
                && userName == row.UserName
                && normalizedUserName == row.NormalizedUserName)
                continue;

            await using var update = connection.CreateCommand();
            update.CommandText = """
                UPDATE "AspNetUsers"
                SET "Email" = @email,
                    "NormalizedEmail" = @normalizedEmail,
                    "UserName" = @userName,
                    "NormalizedUserName" = @normalizedUserName
                WHERE "Id" = @id
                """;
            Add(update, "@email", email);
            Add(update, "@normalizedEmail", normalizedEmail);
            Add(update, "@userName", userName);
            Add(update, "@normalizedUserName", normalizedUserName);
            Add(update, "@id", row.Id);
            updated += await update.ExecuteNonQueryAsync();
        }

        return updated;
    }

    private static async Task<int> EncryptTextAsync(
        System.Data.Common.DbConnection connection,
        IPhiProtector protector,
        string table,
        string column,
        bool deterministic)
    {
        var rows = new List<(object Id, string Text)>();
        await using (var select = connection.CreateCommand())
        {
            select.CommandText = $"""SELECT "Id", "{column}" FROM "{table}" WHERE "{column}" IS NOT NULL""";
            try
            {
                await using var reader = await select.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var text = ReadText(reader, 1);
                    if (string.IsNullOrEmpty(text) || IsCiphertext(text))
                        continue;
                    rows.Add((reader.GetValue(0), Normalize(column, text)));
                }
            }
            catch (Exception)
            {
                return 0;
            }
        }

        var updated = 0;
        foreach (var (id, text) in rows)
        {
            var stored = deterministic ? protector.ProtectDeterministic(text) : protector.Protect(text);
            await using var update = connection.CreateCommand();
            update.CommandText = $"""UPDATE "{table}" SET "{column}" = @value WHERE "Id" = @id""";
            Add(update, "@value", stored);
            Add(update, "@id", id);
            updated += await update.ExecuteNonQueryAsync();
        }

        return updated;
    }

    private static async Task<int> EncryptPhotoAsync(
        System.Data.Common.DbConnection connection,
        IPhiProtector protector,
        string column)
    {
        if (column is not ("ProfilePhotoData" or "FamilyContactPhotoData"))
            return 0;

        var rows = new List<(object Id, byte[] Data)>();
        await using (var select = connection.CreateCommand())
        {
            select.CommandText = $"""
                SELECT "Id", "{column}"
                FROM "PatientProfiles"
                WHERE "{column}" IS NOT NULL
                """;
            try
            {
                await using var reader = await select.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    if (reader.IsDBNull(1))
                        continue;
                    var data = (byte[])reader.GetValue(1);
                    if (data.Length >= 2 && data[0] == (byte)'E' && data[1] == (byte)'1')
                        continue;
                    rows.Add((reader.GetValue(0), data));
                }
            }
            catch (Exception)
            {
                return 0;
            }
        }

        var updated = 0;
        foreach (var (id, data) in rows)
        {
            await using var update = connection.CreateCommand();
            update.CommandText = $"""UPDATE "PatientProfiles" SET "{column}" = @value WHERE "Id" = @id""";
            var value = update.CreateParameter();
            value.ParameterName = "@value";
            value.Value = protector.ProtectBytes(data) ?? data;
            update.Parameters.Add(value);
            Add(update, "@id", id);
            updated += await update.ExecuteNonQueryAsync();
        }

        return updated;
    }

    private static string? SealLookup(IPhiProtector protector, string? value)
    {
        if (string.IsNullOrEmpty(value) || IsCiphertext(value))
            return value;
        return protector.ProtectDeterministic(value);
    }

    private static string Normalize(string column, string value)
    {
        if (!string.Equals(column, "Kind", StringComparison.Ordinal))
            return value;
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            && Enum.IsDefined(typeof(SugarKind), number))
            return ((SugarKind)number).ToString();
        return value;
    }

    private static bool IsCiphertext(string value) =>
        value.StartsWith(PhiProtector.StringPrefix, StringComparison.Ordinal);

    private static string? ReadText(IDataRecord reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return null;
        var raw = reader.GetValue(ordinal);
        return raw switch
        {
            string text => text,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => Convert.ToString(raw, CultureInfo.InvariantCulture)
        };
    }

    private static void Add(System.Data.Common.DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
