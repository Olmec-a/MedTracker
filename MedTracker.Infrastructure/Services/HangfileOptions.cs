namespace MedTracker.Infrastructure.Services;

/// <summary>
/// Управляет режимом работы Hangfire в текущем процессе.
///
/// Hangfire состоит из двух частей: client (enqueue в БД) и server (исполнение worker'ов).
/// В multi-instance K8s-деплое имеет смысл разделить их:
///   - API-pods (replicas: 3+): RunServer=false. Только enqueue, не исполнение.
///   - Worker-pod (replicas: 1): RunServer=true. Запускает воркеры и периодические джобы.
///
/// Это снижает нагрузку на API (нет 32 worker-потоков на под × 3 реплики = 96 потоков),
/// и предотвращает race condition'ы между подами при работе с outbox.
/// </summary>
public class HangfireOptions
{
    /// <summary>
    /// Запускать ли Hangfire server (worker pool + scheduler) в этом процессе.
    /// По умолчанию true для обратной совместимости (docker-compose сетап).
    /// В K8s API-pods переопределяют через env: Hangfire__RunServer=false.
    /// </summary>
    public bool RunServer { get; set; } = true;

    /// <summary>
    /// Количество worker-потоков, если RunServer=true. По умолчанию равно числу CPU × 2.
    /// </summary>
    public int? WorkerCount { get; set; }

    /// <summary>
    /// Регистрировать ли recurring jobs (outbox, cleanup) при старте.
    /// Только в worker-режиме — в API-режиме регистрация бессмысленна
    /// (jobs всё равно исполняются на worker-поде).
    /// </summary>
    public bool RegisterRecurringJobs { get; set; } = true;
}