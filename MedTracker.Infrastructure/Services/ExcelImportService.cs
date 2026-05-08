using MedTracker.Application.DTOs;
using MedTracker.Application.Interfaces;
using MedTracker.Domain.Entities;
using MedTracker.Domain.Exceptions;
using MedTracker.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;

namespace MedTracker.Infrastructure.Services;

/// <summary>
/// Импорт справочника из Excel.
///
/// ⚠️ Этот файл отличается от исходного только тем, что:
///   1. Добавлена зависимость ICatalogCacheInvalidator.
///   2. После успешного импорта вызывается _cacheInvalidator.InvalidateAsync()
///      ДО возврата результата — версия каталога инкрементится в Redis,
///      все ранее закешированные ключи становятся невидимы.
///
/// Если оригинальный ExcelImportService уже у тебя на месте — открой его и
/// добавь только эти два изменения. Полный текст ниже — на случай если хочется
/// заменить целиком.
/// </summary>
public class ExcelImportService : IExcelImportService
{
    private readonly AppDbContext _db;
    private readonly IImportRecordRepository _importRecordRepo;
    private readonly ICatalogCacheInvalidator _cacheInvalidator;
    private readonly ILogger<ExcelImportService> _logger;

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

    public ExcelImportService(
        AppDbContext db,
        IImportRecordRepository importRecordRepo,
        ICatalogCacheInvalidator cacheInvalidator,
        ILogger<ExcelImportService> logger)
    {
        _db = db;
        _importRecordRepo = importRecordRepo;
        _cacheInvalidator = cacheInvalidator;
        _logger = logger;
    }

    public async Task<ImportResultDto> ImportAsync(
        byte[] fileBytes, string fileName, string diagnosisName, Guid importedByUserId,
        CancellationToken ct = default)
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

        var diagnosis = await _db.Diagnoses.FirstOrDefaultAsync(d => d.Name == diagnosisName, ct)
            ?? throw new NotFoundException(nameof(Diagnosis), diagnosisName);

        using var stream = new MemoryStream(fileBytes);
        using var package = new ExcelPackage(stream);

        var worksheet = package.Workbook.Worksheets.FirstOrDefault()
            ?? throw new ExcelImportException("Excel file contains no worksheets.");

        var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
        {
            var headerValue = worksheet.Cells[1, col].Text?.Trim();
            if (!string.IsNullOrEmpty(headerValue))
                headerMap[headerValue] = col;
        }

        var hasInn = headerMap.ContainsKey("Международное непатентованное наименование")
                     || headerMap.ContainsKey("МНН");
        var requiredColumns = new[] {
            "Гормональные препараты", "Торговое наименование", "Доза",
            "Форма применения", "Частота применения" };
        var missingColumns = requiredColumns.Where(c => !headerMap.ContainsKey(c)).ToList();
        if (!hasInn) missingColumns.Add("МНН (или Международное непатентованное наименование)");
        if (missingColumns.Count > 0)
            throw new ExcelImportException($"Missing required columns: {string.Join(", ", missingColumns)}");

        var innColumnName = headerMap.ContainsKey("Международное непатентованное наименование")
            ? "Международное непатентованное наименование"
            : "МНН";

        int medicationsImported = 0;
        int supplementsImported = 0;
        int sideEffectsImported = 0;

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        try
        {
            for (int row = 2; row <= worksheet.Dimension.End.Row; row++)
            {
                var inn = GetCellValue(worksheet, row, headerMap, innColumnName);
                var tradeName = GetCellValue(worksheet, row, headerMap, "Торговое наименование");

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

                var supplementsRaw = GetCellValue(worksheet, row, headerMap, "БАД");
                if (!string.IsNullOrWhiteSpace(supplementsRaw))
                {
                    var supplementNames = supplementsRaw.Split(
                        new[] { ',', ';' },
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var suppName in supplementNames)
                    {
                        await _db.Supplements.AddAsync(new Supplement
                        {
                            MedicationId = medication.Id,
                            Name = suppName,
                            Dosage = string.Empty,
                            Frequency = string.Empty
                        }, ct);
                        supplementsImported++;
                    }
                }

                var sideEffectsRaw = GetCellValue(worksheet, row, headerMap, "Побочные эффекты");
                if (!string.IsNullOrWhiteSpace(sideEffectsRaw))
                {
                    var sideEffectNames = sideEffectsRaw.Split(
                        new[] { ',', ';' },
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var seName in sideEffectNames)
                    {
                        await _db.SideEffects.AddAsync(new SideEffect
                        {
                            MedicationId = medication.Id,
                            Name = seName
                        }, ct);
                        sideEffectsImported++;
                    }
                }
            }

            await _db.SaveChangesAsync(ct);

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

            // Инвалидация кеша — только после успешного commit'а транзакции,
            // чтобы клиенты не подтянули новую версию ключа и не записали в кеш
            // данные, которые могут откатиться.
            await _cacheInvalidator.InvalidateAsync(ct);

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