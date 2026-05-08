namespace MedTracker.Application.Interfaces;

/// <summary>
/// Инвалидирует кеш каталога (Diagnoses/Medications/Supplements/SideEffects).
/// Вызывается после успешного импорта Excel.
///
/// Реализация в Infrastructure через Redis-counter (catalog version).
/// При каждой инвалидации увеличивается версия — все ключи кеша становятся "невидимы",
/// потому что новые запросы строят ключ с новой версией.
/// Старые записи в Redis истекут по TTL.
/// </summary>
public interface ICatalogCacheInvalidator
{
    Task InvalidateAsync(CancellationToken ct = default);
}