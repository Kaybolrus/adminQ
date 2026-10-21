using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace AdminDesk.Services;

/// <summary>
/// Асинхронный мониторинг доступности устройств.
///
/// Ключевые соображения (ТЗ, раздел «Мониторинг доступности»):
///  - проверки выполняются в фоне и не блокируют UI: используется Task.Delay вместо Timer UI-потока,
///    а результаты возвращаются через CancellationToken/коллбэк, который ViewModel маршалирует на UI-поток;
///  - у каждой проверки есть жёсткий тайм-аут (Ping timeout / TCP connect timeout);
///  - одно устройство не может проверяться параллельно самим собой — используется словарь
///    SemaphoreSlim "по одному на устройство" (TryEnter без ожидания: если проверка уже идёт, пропускаем);
///  - общее число одновременных проверок ограничено семафором (_globalGate) — важно при сотнях устройств;
///  - отсутствие ответа на Ping НЕ считается доказательством выключения машины: для сетей,
///    где ICMP запрещён политикой, предусмотрен способ проверки TcpPort.
/// </summary>
public sealed class AvailabilityMonitor : IAsyncDisposable
{
    /// <summary>Интервал между волнами проверок (60 секунд по ТЗ).</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    /// <summary>Тайм-аут одного Ping.</summary>
    private const int PingTimeoutMs = 2000;

    /// <summary>Тайм-аут TCP-подключения.</summary>
    private const int TcpTimeoutMs = 3000;

    /// <summary>Максимум одновременных проверок, чтобы не создавать шторм трафика и сокетов.</summary>
    private const int MaxConcurrentChecks = 8;

    private readonly SemaphoreSlim _globalGate = new(MaxConcurrentChecks, MaxConcurrentChecks);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _perDeviceGates = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;

    /// <summary>Запускает цикл проверок. Первый проход выполняется сразу, без ожидания интервала.</summary>
    public void Start(Func<IReadOnlyList<Models.DeviceEntry>> getDevicesToCheck,
                      Action<Models.DeviceEntry, bool, DateTime> onResult)
    {
        _loopTask ??= Task.Run(() => RunLoopAsync(getDevicesToCheck, onResult, _cts.Token));
    }

    private async Task RunLoopAsync(Func<IReadOnlyList<Models.DeviceEntry>> getDevices,
                                    Action<Models.DeviceEntry, bool, DateTime> onResult,
                                    CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Снимок списка берём через коллбэк на UI-потоке внутри ViewModel.
                var devices = getDevices();
                var checks = devices.Select(async device => await CheckOneAsync(device, onResult, ct));
                await Task.WhenAll(checks);

                await Task.Delay(Interval, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Штатная остановка при закрытии приложения.
        }
    }

    private async Task CheckOneAsync(Models.DeviceEntry device,
                                     Action<Models.DeviceEntry, bool, DateTime> onResult,
                                     CancellationToken ct)
    {
        if (!device.MonitorEnabled)
            return;

        // Защита от параллельных проверок одного устройства: если предыдущая ещё идёт — пропускаем эту волну.
        var gate = _perDeviceGates.GetOrAdd(BuildDeviceKey(device), _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(TimeSpan.Zero, ct))
            return;

        try
        {
            // Ограничение общего числа одновременных проверок.
            await _globalGate.WaitAsync(ct);
            bool reachable;
            try
            {
                reachable = device.CheckMethod == Models.CheckMethod.TcpPort
                    ? await CheckTcpAsync(device.Address, device.TcpPort, ct)
                    : await CheckPingAsync(device.Address, ct);
            }
            finally
            {
                _globalGate.Release();
            }

            // onResult marshalит обновление свойств на UI-поток (см. MainViewModel).
            onResult(device, reachable, DateTime.Now);
        }
        catch (OperationCanceledException)
        {
            // Остановка приложения во время проверки — нормально.
        }
        catch (Exception)
        {
            // Любая непредвиденная ошибка проверки (например, разрешение имени) трактуется как «нет ответа»,
            // но приложение не падает. Это осознанное решение: единичный сбой сети не должен закрывать программу.
            onResult(device, false, DateTime.Now);
        }
        finally
        {
            gate.Release();
        }
    }

    private static string BuildDeviceKey(Models.DeviceEntry device) =>
        $"{device.Address}|{device.CheckMethod}|{device.TcpPort}";

    /// <summary>Ping с тайм-аутом. Отсутствие ответа НЕ доказывает выключение — ICMP может фильтроваться сетью.</summary>
    private static async Task<bool> CheckPingAsync(string address, CancellationToken ct)
    {
        using var ping = new Ping();
        try
        {
            var reply = await ping.SendPingAsync(address, PingTimeoutMs);
            return reply.Status == IPStatus.Success;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // UnresolvedAddress и прочие ошибки разрешения хоста => считаем недоступным.
            return false;
        }
    }

    /// <summary>TCP-проверка указанного порта: позволяет проверить нужную службу, когда ICMP запрещён.</summary>
    private static async Task<bool> CheckTcpAsync(string address, int port, CancellationToken ct)
    {
        using var tcp = new TcpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TcpTimeoutMs);
        try
        {
            await tcp.ConnectAsync(address, port, timeoutCts.Token);
            return tcp.Connected;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Тайм-аут или отказ в подключении => порт не отвечает.
            return false;
        }
    }

    /// <summary>Останавливает цикл и дожидается завершения текущих проверок (с небольшим лимитом).</summary>
    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_loopTask != null)
        {
            try
            {
                await Task.WhenAny(_loopTask, Task.Delay(TimeSpan.FromSeconds(5)));
            }
            catch
            {
                // Исключения цикла уже подавлены OperationCanceledException; здесь страховка.
            }
        }
        _cts.Dispose();
        _globalGate.Dispose();
        foreach (var gate in _perDeviceGates.Values)
            gate.Dispose();
    }
}
