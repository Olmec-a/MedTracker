using MedTracker.Application.DTOs;
using MedTracker.Application.Interfaces;
using MedTracker.Domain.Entities;
using MedTracker.Domain.Exceptions;
using MedTracker.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;

namespace MedTracker.Infrastructure.Services;

public class ExcelImportService : IExcelImportService
{
    private readonly AppDbContext _db;
    private readonly IImportRecordRepository _importRecordRepo;
    private readonly ILogger<ExcelImportService> _logger;

    // Expected column names (case-insensitive matching)
    private static readonly Dictionary<string, string> ColumnMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Гормональные препараты", nameof(Medication.HormonalGroup) },
        { "МНН", nameof(Medication.INN) },
        { "Торговое наименование", nameof(Medication.TradeName) },
        { "Доза", nameof(Medication.Dosage) },
        { "Форма применения", nameof(Medication.Form) },
        { "Частота применения", nameof(Medication.Frequency) },
        { "Диета", nameof(Medication.Diet) },
        { "БАД", "Supplement" },
        { "Побочные эффекты", "SideEffect" }
    };

    public ExcelImportService(AppDbContext db, IImportRecordRepository importRecordRepo, ILogger<ExcelImportService> logger)
    {
        _db = db;
        _importRecordRepo = importRecordRepo;
        _logger = logger;
    }

    public async Task<ImportResultDto> ImportAsync(byte[] fileBytes, string fileName, string diagnosisName, Guid importedByUserId, CancellationToken ct = default)
    {
        ExcelPackage.License.SetNonCommercialOrganization("MedTracker");

        var diagnosis = await _db.Diagnoses.FirstOrDefaultAsync(d => d.Name == diagnosisName, ct)
            ?? throw new NotFoundException(nameof(Diagnosis), diagnosisName);

        using var stream = new MemoryStream(fileBytes);
        using var package = new ExcelPackage(stream);

        var worksheet = package.Workbook.Worksheets.FirstOrDefault()
            ?? throw new ExcelImportException("Excel file contains no worksheets.");

        // Parse header row
        var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
        {
            var headerValue = worksheet.Cells[1, col].Text?.Trim();
            if (!string.IsNullOrEmpty(headerValue))
                headerMap[headerValue] = col;
        }

        // Validate required columns
        var requiredColumns = new[] { "Гормональные препараты", "МНН", "Торговое наименование", "Доза", "Форма применения", "Частота применения" };
        var missingColumns = requiredColumns.Where(c => !headerMap.ContainsKey(c)).ToList();
        if (missingColumns.Count > 0)
            throw new ExcelImportException($"Missing required columns: {string.Join(", ", missingColumns)}");

        int medicationsImported = 0;
        int supplementsImported = 0;
        int sideEffectsImported = 0;

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        try
        {
            for (int row = 2; row <= worksheet.Dimension.End.Row; row++)
            {
                var inn = GetCellValue(worksheet, row, headerMap, "МНН");
                var tradeName = GetCellValue(worksheet, row, headerMap, "Торговое наименование");

                // Skip empty rows
                if (string.IsNullOrWhiteSpace(inn) && string.IsNullOrWhiteSpace(tradeName))
                    continue;

                var medication = new Medication
                {
                    DiagnosisId = diagnosis.Id,
                    HormonalGroup = GetCellValue(worksheet, row, headerMap, "Гормональные препараты"),
                    INN = inn,
                    TradeName = tradeName,
                    Dosage = GetCellValue(worksheet, row, headerMap, "Доза"),
                    Form = GetCellValue(worksheet, row, headerMap, "Форма применения"),
                    Frequency = GetCellValue(worksheet, row, headerMap, "Частота применения"),
                    Diet = GetCellValue(worksheet, row, headerMap, "Диета")
                };

                await _db.Medications.AddAsync(medication, ct);
                medicationsImported++;

                // Parse supplements (comma-separated in БАД column)
                var supplementsRaw = GetCellValue(worksheet, row, headerMap, "БАД");
                if (!string.IsNullOrWhiteSpace(supplementsRaw))
                {
                    var supplementNames = supplementsRaw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var suppName in supplementNames)
                    {
                        var supplement = new Supplement
                        {
                            MedicationId = medication.Id,
                            Name = suppName,
                            Dosage = string.Empty,
                            Frequency = string.Empty
                        };
                        await _db.Supplements.AddAsync(supplement, ct);
                        supplementsImported++;
                    }
                }

                // Parse side effects (comma-separated)
                var sideEffectsRaw = GetCellValue(worksheet, row, headerMap, "Побочные эффекты");
                if (!string.IsNullOrWhiteSpace(sideEffectsRaw))
                {
                    var sideEffectNames = sideEffectsRaw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var seName in sideEffectNames)
                    {
                        var sideEffect = new SideEffect
                        {
                            MedicationId = medication.Id,
                            Name = seName
                        };
                        await _db.SideEffects.AddAsync(sideEffect, ct);
                        sideEffectsImported++;
                    }
                }
            }

            await _db.SaveChangesAsync(ct);

            // Save import record
            var importRecord = new ImportRecord
            {
                FileName = fileName,
                DiagnosisName = diagnosisName,
                RecordsImported = medicationsImported,
                ImportedByUserId = importedByUserId
            };
            await _importRecordRepo.AddAsync(importRecord, ct);
            await _importRecordRepo.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);

            _logger.LogInformation(
                "Import completed: {FileName} for {Diagnosis} — {Meds} medications, {Supps} supplements, {SEs} side effects",
                fileName, diagnosisName, medicationsImported, supplementsImported, sideEffectsImported);

            return new ImportResultDto(true, medicationsImported, supplementsImported, sideEffectsImported,
                $"Successfully imported {medicationsImported} medications, {supplementsImported} supplements, {sideEffectsImported} side effects.");
        }
        catch (Exception ex) when (ex is not ExcelImportException)
        {
            await transaction.RollbackAsync(ct);
            _logger.LogError(ex, "Import failed for {FileName}", fileName);
            throw new ExcelImportException($"Import failed: {ex.Message}", ex);
        }
    }

    public async Task<List<ImportRecordDto>> GetImportHistoryAsync(CancellationToken ct = default)
    {
        var records = await _importRecordRepo.GetAllOrderedAsync(ct);
        return records.Select(r => new ImportRecordDto(
            r.Id, r.FileName, r.DiagnosisName, r.RecordsImported,
            r.ImportedAt, r.ImportedBy.FullName)).ToList();
    }

    private static string GetCellValue(ExcelWorksheet ws, int row, Dictionary<string, int> headerMap, string columnName)
    {
        if (!headerMap.TryGetValue(columnName, out var col))
            return string.Empty;
        return ws.Cells[row, col].Text?.Trim() ?? string.Empty;
    }
}