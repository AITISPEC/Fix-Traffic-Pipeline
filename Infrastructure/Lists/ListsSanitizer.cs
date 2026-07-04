using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PlatformLauncher.Core.Interfaces;
using PlatformLauncher.Domain.Models;

namespace PlatformLauncher.Infrastructure.Lists
{
    /// <summary>
    /// Санитизация списков для zapret: удаление дублируемых записей по include/exclude спискам из config.yaml.
    /// </summary>
    /// <remarks>
    /// Потокобезопасность: HashSet + List&lt;string&gt; — конкурентные операции Read/Write допустимы (на .NET 5+).
    /// Сложность O(n*m) по доменам, где n = строка списка, m = количество wildcard-масок.
    /// </remarks>
    public class ListsSanitizer : IListsSanitizer
    {
        private readonly ILogger _logger;
        private readonly IAppConfigService _appConfigService;

        public ListsSanitizer(ILogger logger, IAppConfigService appConfigService)
        {
            _logger = logger;
            _appConfigService = appConfigService;
        }

        public void Sanitize(string listsPath, GameConfig config)
        {
            if (string.IsNullOrEmpty(listsPath) || !Directory.Exists(listsPath))
                throw new DirectoryNotFoundException($"Папка lists не найдена: {listsPath}");

            var allDomainsToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allIpsToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string fixFilePath = Path.Combine(listsPath, "fix.txt");
            if (File.Exists(fixFilePath))
            {
                try
                {
                    var fixLines = File.ReadAllLines(fixFilePath);
                    foreach (var line in fixLines)
                    {
                        var trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                            allIpsToRemove.Add(trimmed);
                    }
                    // Очищаем файл для новой сессии
                    File.WriteAllText(fixFilePath, string.Empty);
                    _logger.Info($"Прочитано и очищено {allIpsToRemove.Count} сессионных IP из fix.txt");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Ошибка чтения/очистки fix.txt: {ex.Message}");
                }
            }

            // Собираем домены для удаления из include/exclude
            AddDomainsToRemove(allDomainsToRemove, config.IncludeDomains);
            AddDomainsToRemove(allDomainsToRemove, config.ExcludeDomains);
            // pass_ips — IP, но для удаления из файлов добавим их как строки
            if (config.PassIps != null)
                foreach (var ip in config.PassIps)
                    allIpsToRemove.Add(ip.Trim());

            // Собираем IP для удаления
            AddIpsToRemove(allIpsToRemove, config.IncludeIps);
            AddIpsToRemove(allIpsToRemove, config.ExcludeIps);

            // 1. Удаление из всех txt-файлов
            foreach (var file in Directory.GetFiles(listsPath, "*.txt"))
            {
                // Пропускаем fix.txt
                if (Path.GetFileName(file).Equals("fix.txt", StringComparison.OrdinalIgnoreCase))
                    continue;

                var lines = File.ReadAllLines(file);
                var newLines = new List<string>();
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed))
                        continue;

                    // Проверяем совпадение с fix.txt
                    if (allIpsToRemove.Contains(trimmed))
                        continue;

                    if (IsLineToRemove(trimmed, allDomainsToRemove, allIpsToRemove))
                        continue;
                    newLines.Add(line);
                }
                File.WriteAllLines(file, newLines);
            }

            // 2. Добавление include/exclude списков (без перезаписи)
            var appConfig = _appConfigService.Load();
            if (appConfig?.Lists == null)
            {
                _logger.Warning("Секция lists в app_config.yaml отсутствует. Санация пропущена.");
                return;
            }
            var lists = appConfig.Lists;

            AppendDomains(Path.Combine(listsPath, lists.DomainFile), config.IncludeDomains);
            AppendIps(Path.Combine(listsPath, lists.IpFile), config.IncludeIps);
            AppendDomains(Path.Combine(listsPath, lists.ExcludeDomainFile), config.ExcludeDomains);
            AppendIps(Path.Combine(listsPath, lists.ExcludeIpFile), config.ExcludeIps);
        }

        public void WriteCloudflareDomains(string listsPath, List<string> cloudflareDomains)
        {
            if (string.IsNullOrEmpty(listsPath) || !Directory.Exists(listsPath))
                throw new DirectoryNotFoundException($"Папка lists не найдена: {listsPath}");

            // Удаляем все записи, соответствующие cloudflareDomains, из всех txt-файлов
            var domainsToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddDomainsToRemove(domainsToRemove, cloudflareDomains);

            foreach (var file in Directory.GetFiles(listsPath, "*.txt"))
            {
                var lines = File.ReadAllLines(file);
                var newLines = new List<string>();
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    if (IsLineToRemove(trimmed, domainsToRemove, new HashSet<string>()))
                        continue;
                    newLines.Add(line);
                }
                File.WriteAllLines(file, newLines);
            }

            // Записываем cloudflareDomains в list-general.txt (имя из app_config, но пока жёстко)
            var appConfig = _appConfigService.Load();
            if (appConfig?.Lists == null)
            {
                _logger.Warning("Секция lists в app_config.yaml отсутствует.");
                return;
            }
            var generalFile = Path.Combine(listsPath, appConfig.Lists.GeneralDomainFile);
            AppendDomains(generalFile, cloudflareDomains);

            _logger.Info($"Cloudflare домены записаны в {generalFile}");
        }

        private void AddDomainsToRemove(HashSet<string> set, List<string> domains)
        {
            if (domains == null) return;
            foreach (var d in domains)
            {
                var clean = d.Trim();
                set.Add(clean);
                if (clean.StartsWith("*."))
                    set.Add(clean.Substring(2));
            }
        }

        private void AddIpsToRemove(HashSet<string> set, List<string> ips)
        {
            if (ips == null) return;
            foreach (var ip in ips)
                set.Add(ip.Trim());
        }

        private bool IsLineToRemove(string line, HashSet<string> domains, HashSet<string> ips)
        {
            if (ips.Contains(line)) return true;

            foreach (var d in domains)
            {
                // 1. Точное совпадение (для обычных доменов и IP)
                if (line.Equals(d, StringComparison.OrdinalIgnoreCase))
                    return true;

                // 2. Проверка wildcard-масок вида *.domain.com
                if (d.StartsWith("*."))
                {
                    string suffix = d.Substring(1); // Получаем ".domain.com"
                    if (line.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        private void AppendDomains(string filePath, List<string> domains)
        {
            if (domains == null || domains.Count == 0) return;

            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(filePath))
            {
                foreach (var line in File.ReadAllLines(filePath))
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        existing.Add(trimmed);
                }
            }

            var toAdd = new List<string>();
            foreach (var d in domains)
            {
                var clean = d.Trim();
                if (!existing.Contains(clean))
                {
                    toAdd.Add(clean);
                    existing.Add(clean);
                }
                if (clean.StartsWith("*."))
                {
                    var exact = clean.Substring(2);
                    if (!existing.Contains(exact))
                    {
                        toAdd.Add(exact);
                        existing.Add(exact);
                    }
                }
            }

            if (toAdd.Count == 0) return;

            using (var writer = File.AppendText(filePath))
            {
                foreach (var item in toAdd)
                    writer.WriteLine(item);
            }
        }

        private void AppendIps(string filePath, List<string> ips)
        {
            if (ips == null || ips.Count == 0) return;

            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(filePath))
            {
                foreach (var line in File.ReadAllLines(filePath))
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        existing.Add(trimmed);
                }
            }

            var toAdd = new List<string>();
            foreach (var ip in ips)
            {
                var clean = ip.Trim();
                if (!existing.Contains(clean))
                {
                    toAdd.Add(clean);
                    existing.Add(clean);
                }
            }

            if (toAdd.Count == 0) return;

            using (var writer = File.AppendText(filePath))
            {
                foreach (var item in toAdd)
                    writer.WriteLine(item);
            }
        }
    }
}