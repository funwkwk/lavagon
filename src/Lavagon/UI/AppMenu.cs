using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Lavagon.Core;
using Lavagon.Core.Services;

namespace Lavagon.UI
{
    /// <summary>
    /// The Lavagon menu (Menu link, right click on the window and right click on the tray icon).
    /// Rebuilt every time it opens so it always reflects bin\ and www\.
    /// </summary>
    public static class AppMenu
    {
        private static MainForm _form;

        public static void Build(Menu.MenuItemCollection items, MainForm form)
        {
            _form = form;
            items.Clear();

            items.Add(Www());
            if (App.Apache.Installed) items.Add(ApacheMenu());
            if (Php.All.Count > 0) items.Add(PhpMenu());
            if (App.MySql.Installed) items.Add(MySqlMenu());
            if (App.Nginx.Installed) items.Add(NginxMenu());
            foreach (var svc in new ServiceBase[] { App.PostgreSql, App.MongoDb, App.Redis, App.Memcached })
                if (svc.Installed) items.Add(SimpleServiceMenu(svc));
            if (App.Mailpit.Installed) items.Add(MailpitMenu());
            foreach (var (tool, title) in new[] { ("nodejs", "Node.js"), ("python", "Python") })
                if (Versions.List(tool).Count > 0) items.Add(ToolMenu(tool, title));
            items.Add(Tools());
            items.Add(QuickAppMenu());
            items.Add("-");
            items.Add(Item("Preferences...", () => _form.ShowPreferences()));
            items.Add(Item("Exit", () => _form.ExitApp()));
        }

        // ---- helpers ---------------------------------------------------------------------

        private static string Esc(string text) => text.Replace("&", "&&");

        private static MenuItem Item(string text, Action click, bool check = false, bool enabled = true)
        {
            var mi = new MenuItem(Esc(text)) { Checked = check, Enabled = enabled };
            if (click != null) mi.Click += (o, e) => _form.Safe(click);
            return mi;
        }

        private static MenuItem Sub(string text, params MenuItem[] children)
        {
            var mi = new MenuItem(Esc(text));
            foreach (var c in children.Where(c => c != null)) mi.MenuItems.Add(c);
            return mi;
        }

        private static MenuItem Sep() => new MenuItem("-");

        private static MenuItem Dir(string label, string path) => Item("dir: " + label, () => Proc.Explore(path));

        private static MenuItem VersionMenu(string title, List<string> folders, string current, Action<string> select)
        {
            var mi = new MenuItem(Esc(title));
            foreach (var f in folders)
            {
                var folder = f;
                mi.MenuItems.Add(new MenuItem(Esc(folder), (o, e) => _form.Safe(() => select(folder)))
                {
                    RadioCheck = true,
                    Checked = string.Equals(folder, current, StringComparison.OrdinalIgnoreCase),
                });
            }
            mi.MenuItems.Add(Sep());
            mi.MenuItems.Add(Item($"How to add another {title} version", () =>
                Ui.Info($"Download the zip and extract it into its own folder in:\n\n{Path.GetDirectoryName(Path.Combine(AppPaths.Bin, folders.FirstOrDefault() ?? ""))}\n\nIt will show up here.")));
            return mi;
        }

        /// <summary>Switch a service to another version, restarting it if it was running.</summary>
        private static void SwitchServiceVersion(ServiceBase svc, string folder)
        {
            bool wasRunning = svc.IsRunning;
            _form.RunAsync("Switching...", () =>
            {
                if (wasRunning) svc.Stop();
                svc.SelectVersion(folder);
                if (wasRunning) svc.Start();
            }, () => _form.RebuildRows());
        }

        // ---- www -----------------------------------------------------------------------------

        private static MenuItem Www()
        {
            var mi = new MenuItem("www");
            var docRoot = App.Settings.DocumentRoot;
            foreach (var p in VirtualHosts.Projects())
            {
                var project = p;
                var url = VirtualHosts.Url(project);
                mi.MenuItems.Add(Sub(project.Name,
                    Item("Visit site  (" + url + ")", () => Proc.OpenUrl(url)),
                    Item("Explore", () => Proc.Explore(project.Dir)),
                    Item("Terminal", () => Env.OpenTerminal(project.Dir))));
            }
            if (mi.MenuItems.Count > 0) mi.MenuItems.Add(Sep());

            var history = App.Settings.Get("preferences", "DocumentRootHistory").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (!history.Contains(docRoot, StringComparer.OrdinalIgnoreCase)) history.Insert(0, docRoot);
            var switchRoot = new MenuItem("Switch Document Root");
            foreach (var h in history.Take(8))
            {
                var dir = h;
                switchRoot.MenuItems.Add(new MenuItem(Esc(dir), (o, e) => _form.Safe(() => SetDocumentRoot(dir)))
                {
                    RadioCheck = true,
                    Checked = string.Equals(dir, docRoot, StringComparison.OrdinalIgnoreCase),
                });
            }
            switchRoot.MenuItems.Add(Sep());
            switchRoot.MenuItems.Add(Item("Select another...", () =>
            {
                var dir = Ui.PickFolder("Select the new Document Root", docRoot, _form);
                if (dir != null) SetDocumentRoot(dir);
            }));
            mi.MenuItems.Add(switchRoot);
            mi.MenuItems.Add(Item("Document Root  (" + docRoot + ")", () => Proc.Explore(docRoot)));
            return mi;
        }

        public static void SetDocumentRoot(string dir)
        {
            var history = App.Settings.Get("preferences", "DocumentRootHistory").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            history.RemoveAll(h => string.Equals(h, dir, StringComparison.OrdinalIgnoreCase));
            history.Insert(0, dir);
            App.Settings.Set("preferences", "DocumentRootHistory", string.Join("|", history.Take(8)));
            App.Settings.DocumentRoot = dir;
            _form.RestartWebServers();
        }

        // ---- Apache --------------------------------------------------------------------------

        private static MenuItem ApacheMenu()
        {
            var apache = App.Apache;
            return Sub("Apache",
                VersionMenu("Version", apache.AllVersions, apache.Folder, f => SwitchServiceVersion(apache, f)),
                Sep(),
                Item("httpd.conf (generated, read only)", () => Proc.Edit(apache.ConfFile), enabled: File.Exists(apache.ConfFile)),
                Item("custom.conf", () => Proc.Edit(apache.CustomConf)),
                Dir("sites-enabled", apache.SitesDir),
                Dir("logs", AppPaths.Logs),
                Dir("conf", Path.Combine(apache.Dir, "conf")),
                Sep(),
                Sub("SSL",
                    Item("Enabled", () =>
                    {
                        apache.SslEnabled = !apache.SslEnabled;
                        if (apache.IsRunning) _form.RunAsync("Reloading...", apache.Restart);
                        else _form.RefreshState();
                    }, check: apache.SslEnabled),
                    Sep(),
                    Item("Add Lavagon CA to Trust Store", () =>
                    {
                        Certificates.TrustCa();
                        Ui.Info("Lavagon Local CA was added to your trusted root certificates.\nRestart your browser.");
                    }),
                    Dir("ssl", Certificates.Dir)),
                Sep(),
                Item("Reload", () => _form.RunAsync("Reloading...", apache.Restart), enabled: apache.IsRunning));
        }

        // ---- PHP -----------------------------------------------------------------------------

        private static MenuItem PhpMenu()
        {
            var mi = new MenuItem("PHP");
            mi.MenuItems.Add(VersionMenu("Version", Php.All, Php.Folder, folder =>
            {
                var previous = Php.Folder;
                Php.Select(folder);
                try
                {
                    Php.CheckRuntime();
                    Php.EnsureIni();
                }
                catch
                {
                    Php.Select(previous); // keep the working version
                    throw;
                }
                _form.RestartWebServers();
                var my = App.MySql;
                if (Php.VersionNumber < new System.Version(7, 1) && my.Installed && !my.IsMariaDb && my.VersionNumber >= new System.Version(8, 4))
                    Ui.Info($"Switched to PHP {Php.Version}.\n\nNote: PHP 5.x / 7.0 cannot connect to {my.Title} {my.Version} at all - MySQL 8.4+ always " +
                            "announces caching_sha2_password, which these old clients don't understand.\n\n" +
                            "For these PHP versions put MariaDB (or MySQL 5.7 / 8.0) into bin\\mysql\\ and select it in Menu > MySQL > Version.");
                else if (Php.VersionNumber < new System.Version(7, 4) && my.Installed && my.LegacyAuthApplies && !my.LegacyAuth)
                    _form.Notify($"Switched to PHP {Php.Version}.\nPHP < 7.4 can't log in to {my.Title} {my.Version} by default: enable Menu > {my.Title} > Legacy authentication.");
                else
                    _form.Notify($"Switched to PHP {Php.Version}");
            }));
            if (Php.Installed)
            {
                mi.MenuItems.Add(Sep());
                mi.MenuItems.Add(Item("php.ini", () => { Php.EnsureIni(); Proc.Edit(Php.IniPath); }));

                var ext = new MenuItem("Extensions");
                int i = 0;
                foreach (var kv in Php.Extensions())
                {
                    var name = kv.Key;
                    bool on = kv.Value;
                    var item = Item(name, () =>
                    {
                        var problem = Php.SetExtension(name, !on);
                        if (problem != null)
                        {
                            Php.SetExtension(name, false);
                            throw new LavagonException(problem + "\n\nThe extension was left disabled.");
                        }
                        _form.RestartWebServers();
                    }, check: on);
                    if (i > 0 && i % 25 == 0) item.BarBreak = true;
                    ext.MenuItems.Add(item);
                    i++;
                }
                mi.MenuItems.Add(ext);

                mi.MenuItems.Add(Sub("Quick settings",
                    QuickSetting("memory_limit", "128M", "256M", "512M", "1G", "-1"),
                    QuickSetting("upload_max_filesize", "8M", "64M", "256M", "1G", "2G"),
                    QuickSetting("post_max_size", "8M", "64M", "256M", "1G", "2G"),
                    QuickSetting("max_execution_time", "30", "120", "300", "600", "0"),
                    QuickSetting("display_errors", "On", "Off"),
                    QuickSetting("short_open_tag", "On", "Off")));

                mi.MenuItems.Add(Dir("ext", Path.Combine(Php.Dir, "ext")));
                mi.MenuItems.Add(Dir("php", Php.Dir));
            }
            return mi;
        }

        private static MenuItem QuickSetting(string key, params string[] values)
        {
            var current = Php.GetDirective(key);
            var mi = new MenuItem(Esc(key) + (current.Length > 0 ? "  [" + Esc(current) + "]" : ""));
            foreach (var v in values)
            {
                var value = v;
                mi.MenuItems.Add(new MenuItem(Esc(value), (o, e) => _form.Safe(() =>
                {
                    Php.UpdateDirective(key, value);
                    _form.RestartWebServers();
                }))
                { RadioCheck = true, Checked = string.Equals(current, value, StringComparison.OrdinalIgnoreCase) });
            }
            return mi;
        }

        // ---- MySQL ---------------------------------------------------------------------------

        private static MenuItem MySqlMenu()
        {
            var my = App.MySql;
            return Sub(my.Title,
                VersionMenu("Version", my.AllVersions, my.Folder, f => SwitchServiceVersion(my, f)),
                Sep(),
                Item("my.ini", () => Proc.Edit(my.IniFile)),
                Item("Create database...", CreateDatabase),
                Item("Change root password...", ChangeRootPassword),
                Item("Get root password", () =>
                {
                    Clipboard.SetText(my.RootPassword.Length > 0 ? my.RootPassword : " ");
                    _form.Notify(my.RootPassword.Length > 0 ? "The MySQL root password has been copied to the clipboard" : "The MySQL root password is empty");
                }),
                Item("Backup all databases", BackupDatabases),
                my.LegacyAuthApplies
                    ? Item("Legacy authentication (for PHP < 7.4)", () =>
                    {
                        bool enable = !my.LegacyAuth;
                        if (!my.IsRunning) throw new LavagonException($"Please start {my.Title} first!");
                        _form.RunAsync("Restarting...", () => my.SetLegacyAuth(enable), () => _form.Notify(enable
                            ? "root now uses mysql_native_password - PHP 5.6 to 7.3 can connect."
                            : "root now uses caching_sha2_password (MySQL 8 default)."));
                    }, check: my.LegacyAuth, enabled: my.LegacyAuthAvailable)
                    : null,
                Sep(),
                Dir("data", my.DataDir),
                Dir("backup", Path.Combine(AppPaths.Root, "backup")));
        }

        private static void CreateDatabase()
        {
            var name = Ui.Prompt("Create database", "Database Name", "", owner: _form);
            if (string.IsNullOrEmpty(name)) return;
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z0-9_\-\$]+$"))
                throw new LavagonException("The name is not valid!");
            var r = App.MySql.Sql($"CREATE DATABASE `{name}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
            if (!r.Ok) throw new LavagonException($"Could not create database {name}. Reason:\n{r.Output}");
            _form.Notify("Created database! " + name);
        }

        private static void ChangeRootPassword()
        {
            var my = App.MySql;
            var pwd = Ui.Prompt("Change root password", "New Password (leave empty for no password)", "", password: true, owner: _form);
            if (pwd == null) return;
            var r = my.Sql(my.SetPasswordSql(pwd));
            if (!r.Ok) throw new LavagonException("Could not change MySQL root password. Reason:\n" + r.Output);
            my.RootPassword = pwd;
            _form.Notify("Changed MySQL root password successfully!");
        }

        private static void BackupDatabases()
        {
            var my = App.MySql;
            if (!my.IsRunning) throw new LavagonException($"Please start {my.Title} first!");
            if (my.DumpExe == null) throw new LavagonException("mysqldump.exe was not found.");
            var dir = Path.Combine(AppPaths.Root, "backup");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"all-databases-{DateTime.Now:yyyyMMdd-HHmmss}.sql");
            _form.RunAsync("Backing up...", () =>
            {
                var cnf = Path.Combine(AppPaths.Tmp, "lavagon-dump.cnf");
                File.WriteAllText(cnf, "[client]\r\nuser=root\r\npassword=\"" + my.RootPassword.Replace("\"", "\\\"") + "\"\r\n");
                try
                {
                    var r = Proc.Run(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                        $"/c \"\"{my.DumpExe}\" --defaults-file=\"{cnf}\" -h 127.0.0.1 -P {my.TcpPort} --all-databases --routines --events > \"{file}\"\"",
                        my.BinDir, 600000);
                    if (!r.Ok) throw new LavagonException("Backup failed:\n" + r.Output);
                }
                finally
                {
                    File.Delete(cnf);
                }
            }, () => _form.Notify("All databases have been backed up to " + file));
        }

        // ---- Nginx & others --------------------------------------------------------------------

        private static MenuItem NginxMenu()
        {
            var nginx = App.Nginx;
            return Sub("Nginx",
                VersionMenu("Version", nginx.AllVersions, nginx.Folder, f => SwitchServiceVersion(nginx, f)),
                Sep(),
                Item("nginx.conf (generated, read only)", () => Proc.Edit(nginx.ConfFile), enabled: File.Exists(nginx.ConfFile)),
                Item("custom.conf", () => Proc.Edit(nginx.CustomConf)),
                Dir("sites-enabled", nginx.SitesDir),
                Dir("logs", AppPaths.Logs),
                Sep(),
                Item("Reload", () => _form.RunAsync("Reloading...", nginx.Restart), enabled: nginx.IsRunning));
        }

        private static MenuItem SimpleServiceMenu(ServiceBase svc) =>
            Sub(svc.Title,
                VersionMenu("Version", svc.AllVersions, svc.Folder, f => SwitchServiceVersion(svc, f)),
                Dir(svc.Title.ToLowerInvariant(), svc.Dir));

        private static MenuItem MailpitMenu()
        {
            var mp = App.Mailpit;
            return Sub("Mailpit",
                Item("Web UI  (" + mp.WebUrl + ")", () =>
                {
                    if (!mp.IsRunning) throw new LavagonException("You need to enable and start Mailpit!");
                    Proc.OpenUrl(mp.WebUrl);
                }),
                Item($"SMTP: 127.0.0.1:{mp.SmtpPort} (already set in php.ini)", null, enabled: false),
                VersionMenu("Version", mp.AllVersions, mp.Folder, f => SwitchServiceVersion(mp, f)));
        }

        private static MenuItem ToolMenu(string tool, string title)
        {
            var current = Versions.Resolve(tool, App.Settings.Get(tool, "Version"));
            return Sub(title,
                VersionMenu("Version", Versions.List(tool), current, f =>
                {
                    App.Settings.Set(tool, "Version", f);
                    _form.Notify($"{title} {Versions.Parse(f)} selected. New terminals will use it.");
                }),
                Dir(tool, Path.Combine(AppPaths.Bin, tool, current ?? "")));
        }

        // ---- Tools ---------------------------------------------------------------------------

        private static MenuItem Tools()
        {
            return Sub("Tools",
                Sub("Path",
                    Item("Add Lavagon to Path", () =>
                    {
                        Env.AddToUserPath();
                        Ui.Info("Added Lavagon to Path! New terminals will see the tools (you may need to log off and log on again).");
                    }),
                    Item("Remove Lavagon from Path", () =>
                    {
                        Env.RemoveFromUserPath();
                        Ui.Info("Removed Lavagon from Path!");
                    }),
                    Item("Manage Path", () => Proc.Shell("rundll32.exe", "sysdm.cpl,EditEnvironmentVariables"))),
                Item("Edit hosts file (Administrator)", HostsFile.EditAsAdmin),
                Item("Terminal", () => Env.OpenTerminal()),
                Sep(),
                Item("lavagon.ini", () => Proc.Edit(AppPaths.ConfigFile)),
                Dir("lavagon", AppPaths.Root),
                Dir("bin", AppPaths.Bin),
                Dir("etc", AppPaths.Etc),
                Dir("logs", AppPaths.Logs));
        }

        // ---- Quick app -------------------------------------------------------------------------

        private static MenuItem QuickAppMenu()
        {
            var mi = new MenuItem("Quick app");
            foreach (var r in QuickApp.Recipes())
            {
                var recipe = r;
                mi.MenuItems.Add(Item(recipe.Name, () => CreateQuickApp(recipe)));
            }
            mi.MenuItems.Add(Sep());
            mi.MenuItems.Add(Item("Configuration", () => Proc.Edit(AppPaths.SitesFile)));
            return mi;
        }

        private static void CreateQuickApp(QuickApp.Recipe recipe)
        {
            var name = Ui.Prompt("Quick app: " + recipe.Name, "Project Name", recipe.Name.ToLowerInvariant().Replace(" ", "") + "1", owner: _form);
            if (string.IsNullOrEmpty(name)) return;
            if (!QuickApp.IsValidName(name)) throw new LavagonException("The name is not valid!");
            string created = null;
            _form.RunAsync("Creating...", () =>
            {
                created = QuickApp.Create(recipe, name);
                if (App.Apache.Installed && App.Apache.IsRunning) App.Apache.Restart();
                if (App.Nginx.Installed && App.Nginx.IsRunning) App.Nginx.Restart();
            }, () =>
            {
                if (created == null) return;
                var project = VirtualHosts.Projects().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                var url = project != null ? VirtualHosts.Url(project) : null;
                if (url != null && Ui.Confirm($"Created {name}!\n\n{created}\n\nVisit {url} now?")) Proc.OpenUrl(url);
            });
        }
    }
}
