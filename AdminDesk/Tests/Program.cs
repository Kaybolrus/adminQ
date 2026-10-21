// Инфраструктурный тест-раннер для проверки логики AdminDesk на Linux (без WPF).
// Компилирует Models + Services/Validation + Services/DataService + Services/AvailabilityMonitor
// (те же файлы, что и AdminDesk.Core.csproj) и выполняет сценарии:
//   T1–T5  — DataService: seed, roundtrip, бэкап, повреждение+восстановление, двойное повреждение (ReadOnly)
//   T6–T8  — Validation: IP/порт/пустые значения/UNC
//   T9     — AvailabilityMonitor: корректные статусы Ping/TCP на недостижимых адресах
// Выход 0 = все тесты пройдены.
using System.Net.NetworkInformation;
using System.Text.Json;
using AdminDesk.Models;
using AdminDesk.Services;

int passed = 0, failed = 0;
void Check(string name, bool ok, string? detail = null)
{
    if (ok) { passed++; Console.WriteLine($"PASS  {name}"); }
    else { failed++; Console.WriteLine($"FAIL  {name}{(detail != null ? " :: " + detail : "")}"); }
}

string root = Path.Combine(Path.GetTempPath(), "admindesk-tests-" + Guid.NewGuid().ToString("N").Substring(0, 8));
Directory.CreateDirectory(root);

// ---------------- T1: первый запуск — seed и немедленное сохранение ----------------
var dir1 = Path.Combine(root, "t1");
var svc1 = new DataService(dir1);
var r1 = svc1.Load();
Check("T1a seed: 1 устройство alttest10", r1.Data.Devices.Count == 1 && r1.Data.Devices[0].Name == "alttest10" && r1.Data.Devices[0].Address == "192.168.10.66");
Check("T1b seed: RDP разрешён, мониторинг включён, Ping", r1.Data.Devices[0].RdpAllowed && r1.Data.Devices[0].MonitorEnabled && r1.Data.Devices[0].CheckMethod == CheckMethod.Ping);
Check("T1c seed: 4 стартовые папки с путями \\\\DS2", r1.Data.Folders.Count == 4
      && r1.Data.Folders.Any(f => f.Path == @"\\DS2\FS-1\install")
      && r1.Data.Folders.Any(f => f.Path == @"\\DS2\Scan"));
Check("T1d файл data.json создан при первом запуске", File.Exists(Path.Combine(dir1, "data.json")));
Check("T1e без сообщений об ошибках", r1.Message == null && !r1.ReadOnlyMode, r1.Message);

// ---------------- T2: roundtrip — изменение и повторная загрузка ----------------
var dev = r1.Data.Devices[0];
dev.Name = "renamed-pc";
dev.CheckMethod = CheckMethod.TcpPort;
dev.TcpPort = 445;
r1.Data.Folders.RemoveAt(0);
Check("T2a сохранение без ошибок", svc1.Save(r1.Data) == null);
var r2 = new DataService(dir1).Load();
Check("T2b изменения пережили перезагрузку", r2.Data.Devices.Single().Name == "renamed-pc"
      && r2.Data.Devices.Single().CheckMethod == CheckMethod.TcpPort
      && r2.Data.Devices.Single().TcpPort == 445
      && r2.Data.Folders.Count == 3);
Check("T2c статусы мониторинга НЕ сохраняются в JSON",
      !File.ReadAllText(Path.Combine(dir1, "data.json")).Contains("\"Status\""));

// ---------------- T3: резервная копия создаётся перед заменой ----------------
Check("T3a data.backup.json существует после второго сохранения", File.Exists(Path.Combine(dir1, "data.backup.json")));
var backupJson = File.ReadAllText(Path.Combine(dir1, "data.backup.json"));
// Те же опции, что использует DataService (enum'ы сериализуются строками)
var jsonOpts = new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
var backupData = JsonSerializer.Deserialize<AppData>(backupJson, jsonOpts);
Check("T3b бэкап содержит ПРЕДЫДУЩЕе состояние (seed)", backupData?.Devices[0].Name == "alttest10", backupData?.Devices?[0]?.Name);

// ---------------- T4: повреждение основного файла → восстановление из бэкапа ----------------
File.WriteAllText(Path.Combine(dir1, "data.json"), "{ это не json !!!");
var r4 = new DataService(dir1).Load();
Check("T4a CorruptedOrMissing=true", r4.CorruptedOrMissing);
Check("T4b данные восстановлены из бэкапа (seed)", r4.Data.Devices[0].Name == "alttest10");
Check("T4c сообщение пользователю выдано", r4.Message != null && r4.Message.Contains("резервн"), r4.Message);
Check("T4d НЕ режим ReadOnly (бэкап цел)", !r4.ReadOnlyMode);
var damaged = Directory.GetFiles(dir1, "data.json.damaged-*");
Check("T4e повреждённый файл сохранён как .damaged-*", damaged.Length == 1);
Check("T4f повреждённый оригинал НЕ удалён", File.ReadAllText(Path.Combine(dir1, "data.json")) == "{ это не json !!!");

// ---------------- T5: двойное повреждение (основной + бэкап) → ReadOnly, без перезаписи ----------------
var dir5 = Path.Combine(root, "t5");
var svc5 = new DataService(dir5);
svc5.Load(); // seed + save
svc5.Save(svc5.Load().Data); // ещё одно сохранение, чтобы появился бэкап
File.WriteAllText(Path.Combine(dir5, "data.json"), "{{{ битый");
File.WriteAllText(Path.Combine(dir5, "data.backup.json"), "}}} тоже битый");
var r5 = new DataService(dir5).Load();
Check("T5a ReadOnlyMode=true при двойном повреждении", r5.ReadOnlyMode);
Check("T5b сообщение объясняет ситуацию", r5.Message != null && r5.Message.Contains("БЕЗ СОХРАНЕНИЯ"), r5.Message);
Check("T5c битый основной файл не перезаписан", File.ReadAllText(Path.Combine(dir5, "data.json")) == "{{{ битый");

// ---------------- T6–T8: валидация ----------------
DeviceEntry D(string n, string a, CheckMethod m = CheckMethod.Ping, int port = 3389)
{
    var d = new DeviceEntry { Name = n, Address = a, CheckMethod = m, TcpPort = port };
    return d;
}

Check("T6a пустое имя отклонено", Validation.ValidateDevice(D("", "1.1.1.1"), Array.Empty<DeviceEntry>()) != null);
Check("T6b пустой адрес отклонён", Validation.ValidateDevice(D("x", "  "), Array.Empty<DeviceEntry>()) != null);
Check("T6c корректный IPv4 принят", Validation.ValidateDevice(D("x", "192.168.10.66"), Array.Empty<DeviceEntry>()) == null);
Check("T6d IPv4 с октетом 999 отклонён", Validation.ValidateDevice(D("x", "192.168.10.999"), Array.Empty<DeviceEntry>()) != null);
Check("T6e «похожий на IP» 1.2.3 отклонён", Validation.ValidateDevice(D("x", "1.2.3"), Array.Empty<DeviceEntry>()) != null);
Check("T6f DNS-имя принято", Validation.ValidateDevice(D("x", "server01.corp.local"), Array.Empty<DeviceEntry>()) == null);
Check("T6g адрес с недопустимыми символами отклонён", Validation.ValidateDevice(D("x", "host; rm -rf /"), Array.Empty<DeviceEntry>()) != null);
Check("T7a порт 0 отклонён", Validation.ValidateDevice(D("x", "1.1.1.1", CheckMethod.TcpPort, 0), Array.Empty<DeviceEntry>()) != null);
Check("T7b порт 70000 отклонён", Validation.ValidateDevice(D("x", "1.1.1.1", CheckMethod.TcpPort, 70000), Array.Empty<DeviceEntry>()) != null);
Check("T7c порт 445 принят", Validation.ValidateDevice(D("x", "1.1.1.1", CheckMethod.TcpPort, 445), Array.Empty<DeviceEntry>()) == null);
Check("T7d дубликат имени отклонён", Validation.ValidateDevice(D("dup", "1.1.1.1"), new[] { D("Dup", "2.2.2.2") }) != null);

FolderEntry F(string n, string p)
{
    var f = new FolderEntry { Name = n, Path = p };
    return f;
}
Check("T8a UNC-путь принят", Validation.ValidateFolder(F("шара", @"\\DS2\FS-1"), Array.Empty<FolderEntry>()) == null);
Check("T8b относительный путь отклонён", Validation.ValidateFolder(F("шара", @"folder\sub"), Array.Empty<FolderEntry>()) != null);
Check("T8c пустой путь отклонён", Validation.ValidateFolder(F("шара", ""), Array.Empty<FolderEntry>()) != null);
Check("T8d пустое название отклонено", Validation.ValidateFolder(F("", @"\\a\b"), Array.Empty<FolderEntry>()) != null);

// ---------------- T9: AvailabilityMonitor — асинхронные проверки без зависания ----------------
var offlineDev = D("offline-dev", "192.0.2.1");
offlineDev.MonitorEnabled = true;                 // TEST-NET-1, не отвечает
var tcpDev = D("tcp-dev", "192.0.2.1", CheckMethod.TcpPort, 9);
tcpDev.MonitorEnabled = true;                     // порт 9 не слушается
var disabledDev = D("disabled-dev", "192.0.2.1");
disabledDev.MonitorEnabled = false;
var results = new List<(string name, bool ok)>();
var monitor = new AvailabilityMonitor();
monitor.Start(() => new DeviceEntry[] { offlineDev, tcpDev, disabledDev },
              (d, reachable, _) => { lock (results) results.Add((d.Name, reachable)); });
await Task.Delay(12000); // ping timeout 2s + tcp timeout 3s с запасом
await monitor.DisposeAsync();
Check("T9a Ping-устройство получило статус «нет ответа»", results.Any(r => r.name == "offline-dev" && !r.ok));
Check("T9b TCP-устройство получило статус «нет ответа»", results.Any(r => r.name == "tcp-dev" && !r.ok));
Check("T9c устройство с выключенным мониторингом НЕ проверяется", !results.Any(r => r.name == "disabled-dev"));

Directory.Delete(root, recursive: true);
Console.WriteLine($"\nИТОГ: {passed} пройдено, {failed} провалено.");
return failed == 0 ? 0 : 1;
