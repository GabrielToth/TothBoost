using TothBoost.Core.Known;
using TothBoost.Core.Models;
using TothBoost.Core.Scanners;
using TothBoost.Core.Snapshot;

namespace TothBoost.Core
{
    /// <summary>
    /// Camada de orquestração: prepara o snapshot inicial, classifica serviços,
    /// aplica ações e restaura o sistema. Garante que nenhuma modificação
    /// aconteça antes do snapshot de linha de base existir.
    /// </summary>
    public sealed class BoostEngine
    {
        private readonly ServiceScanner _svcScanner = new();
        private readonly TaskScanner _taskScanner = new();

        public bool BaselineExists => SnapshotService.BaselineExists;

        /// <summary>Verdadeiro se o primeiro uso (ponto de restauração + perfil inicial) já foi concluído.</summary>
        public bool IsInitialized => RestorePointService.IsInitialized;

        /// <summary>
        /// Rotina OBRIGATÓRIA de primeiro uso, na ordem de segurança:
        /// 1) cria o ponto de restauração do Windows;
        /// 2) faz a primeira varredura completa;
        /// 3) grava o perfil inicial com o estado ORIGINAL de TODOS os
        ///    serviços, tarefas agendadas, inicialização e ajustes;
        /// 4) marca a máquina como inicializada — só depois disso o
        ///    usuário pode modificar qualquer coisa.
        /// Retorna (pontoOk, mensagem).
        /// </summary>
        public (bool RestorePointOk, string Message) InitialSetup()
        {
            var (rpOk, rpMsg) = RestorePointService.CreateInitialPoint();
            if (rpOk)
            {
                try { RestorePointService.MarkInitialized(); } catch { }
            }

            // A linha de base agora é criada SÓ depois do ponto de restauração,
            // e nunca sobrescreve um perfil já existente.
            EnsureBaseline();
            return (rpOk, rpMsg);
        }

        /// <summary>
        /// Cria a linha de base (estado original) automaticamente na primeira
        /// execução — idempotente e nunca sobrescreve um snapshot existente.
        /// </summary>
        public void EnsureBaseline()
        {
            if (SnapshotService.BaselineExists) return;
            var services = GetServices();
            var tasks = GetTasks();
            var startups = new StartupScanner().ScanAll();
            SnapshotService.EnsureBaseline(services, tasks, startups);
        }

        /// <summary>Cria um ponto de restauração manual a qualquer momento.</summary>
        public (bool Ok, string Message) CreateRestorePointNow() => RestorePointService.CreateInitialPoint();

        public List<ServiceInfo> GetServices()
        {
            var raw = _svcScanner.ScanAll();
            foreach (var s in raw) KnowledgeBase.Classify(s);
            return raw;
        }

        public List<ProcessInfo> GetProcesses() => new ProcessScanner().ScanAll();

        public List<ScheduledTaskInfo> GetTasks() => _taskScanner.ScanAll();

        // ---------- perfis ----------

        public string CurrentProfileName => ProfileService.CurrentName() ?? "Default";

        /// <summary>Garante o perfil Default com as configurações salvas do usuário atual.</summary>
        public void EnsureDefaultProfile()
        {
            ProfileService.EnsureDefault();
            var name = ProfileService.CurrentName() ?? "Default";
            if (!ProfileService.CaptureExists(name)) SaveProfileCapture();
        }

        public void SaveProfileCapture()
        {
            SaveProfileCaptureTo(null);
        }

        public void SaveProfileCaptureTo(string? profileName)
        {
            var capture = new ProfileCapture
            {
                Services = GetServices()
                    .Select(s => new ProfileServiceState { Name = s.Name, StartKind = (int)s.StartKind, Protected = s.IsProtected })
                    .ToList(),
                Tasks = GetTasks()
                    .Select(t => new ProfileTaskState { FullPath = t.FullPath, Name = t.Name, Enabled = t.Enabled })
                    .ToList(),
                Startups = GetStartups()
                    .Select(i => new ProfileStartupState { Name = i.Name, Location = i.Location, Exists = i.Existing, DisabledByTothBoost = i.DisabledByTothBoost })
                    .ToList(),
                Tweaks = GetTweaks()
                    .Select(t => new ProfileTweakState { Id = t.Id, Applied = t.IsApplied })
                    .ToList()
            };
            var target = profileName ?? ProfileService.CurrentName() ?? "Default";
            ProfileService.SaveCapture(target, capture);
            ProfileService.MarkSaved(target);
        }

        public List<ProfileService.ProfileMeta> ListProfiles() => ProfileService.List();

        public void CreateProfile(string name) => ProfileService.Create(name);

        public void SelectProfile(string name) => ProfileService.Select(name);

        /// <summary>
        /// Aplica todas as configurações gravadas em um perfil (serviços, tarefas, inicialização e ajustes).
        /// </summary>
        public ActionReport ApplyProfile(string profileName)
        {
            var report = new ActionReport();
            EnsureBaseline();
            var capture = ProfileService.LoadCapture(profileName);
            if (capture is null)
            {
                report.Add(profileName, "carregar perfil", false, "Perfil não encontrado ou sem dados salvos.");
                return report;
            }

            // 1. Serviços
            var currentServices = GetServices();
            foreach (var sState in capture.Services)
            {
                var current = currentServices.FirstOrDefault(x => x.Name.Equals(sState.Name, StringComparison.OrdinalIgnoreCase));
                if (current is null || current.IsProtected) continue;

                var targetStart = (ServiceStartKind)sState.StartKind;
                if (targetStart == ServiceStartKind.Disabled)
                {
                    if (current.StartKind != ServiceStartKind.Disabled || current.RunningState == "Executando")
                    {
                        try
                        {
                            ServiceActions.ChangeStartKind(current.Name, ServiceStartKind.Disabled);
                            report.Add(current.Name, "perfil → desabilitado", true);
                            if (current.RunningState == "Executando")
                            {
                                try { ServiceActions.Stop(current.Name); report.Add(current.Name, "parado", true); }
                                catch (Exception ex) { report.AddError(current.Name, "parar", ex); }
                            }
                        }
                        catch (Exception ex) { report.AddError(current.Name, "perfil serviços", ex); }
                    }
                }
                else
                {
                    if (current.StartKind != targetStart)
                    {
                        try
                        {
                            ServiceActions.ChangeStartKind(current.Name, targetStart);
                            report.Add(current.Name, $"perfil → {targetStart}", true);
                        }
                        catch (Exception ex) { report.AddError(current.Name, "perfil serviços", ex); }
                    }
                }
            }

            // 2. Tarefas Agendadas
            var currentTasks = GetTasks();
            foreach (var tState in capture.Tasks)
            {
                var current = currentTasks.FirstOrDefault(x => x.FullPath.Equals(tState.FullPath, StringComparison.OrdinalIgnoreCase));
                if (current is null) continue;
                if (current.Enabled != tState.Enabled)
                {
                    try
                    {
                        TaskActions.SetEnabled(current.FullPath, tState.Enabled);
                        report.Add(current.Name, tState.Enabled ? "perfil → habilitar tarefa" : "perfil → desabilitar tarefa", true);
                    }
                    catch (Exception ex) { report.AddError(current.Name, "perfil tarefas", ex); }
                }
            }

            // 3. Inicialização
            var currentStartups = GetStartups();
            foreach (var stState in capture.Startups)
            {
                var current = currentStartups.FirstOrDefault(x => x.Name.Equals(stState.Name, StringComparison.OrdinalIgnoreCase));
                if (current is null) continue;
                if (stState.DisabledByTothBoost && !current.DisabledByTothBoost)
                {
                    try
                    {
                        StartupScanner.Disable(current);
                        report.Add(current.Name, "perfil → desabilitar inicialização", true);
                    }
                    catch (Exception ex) { report.AddError(current.Name, "perfil inicialização", ex); }
                }
                else if (!stState.DisabledByTothBoost && current.DisabledByTothBoost)
                {
                    try
                    {
                        StartupScanner.Enable(current);
                        report.Add(current.Name, "perfil → habilitar inicialização", true);
                    }
                    catch (Exception ex) { report.AddError(current.Name, "perfil inicialização", ex); }
                }
            }

            // 4. Ajustes (Tweaks)
            foreach (var twState in capture.Tweaks)
            {
                if (Enum.TryParse<TweakKind>(twState.Id, out var kind))
                {
                    var isApplied = TweakManager.IsApplied(kind);
                    if (twState.Applied && !isApplied)
                    {
                        try
                        {
                            TweakManager.Apply(kind);
                            report.Add(TweakCatalog.Get(twState.Id).Name, "perfil → aplicar ajuste", true);
                        }
                        catch (Exception ex) { report.AddError(twState.Id, "perfil ajuste", ex); }
                    }
                    else if (!twState.Applied && isApplied)
                    {
                        try
                        {
                            TweakManager.Revert(kind);
                            report.Add(TweakCatalog.Get(twState.Id).Name, "perfil → reverter ajuste", true);
                        }
                        catch (Exception ex) { report.AddError(twState.Id, "perfil ajuste", ex); }
                    }
                }
            }

            ProfileService.Select(profileName);
            return report;
        }

        // ---------- Pontos de restauração ----------

        public List<RestorePointInfo> ListRestorePoints() => RestorePointService.ListPoints();

        public (bool Ok, string Message) DeleteRestorePoint(long sequence) => RestorePointService.DeletePoint(sequence);

        public List<StartupItem> GetStartups() => new StartupScanner().ScanAll();

        public List<TweakInfo> GetTweaks()
        {
            var all = TweakCatalog.All();
            foreach (var t in all)
            {
                if (Enum.TryParse<TweakKind>(t.Id, out var kind))
                {
                    t.Applies = true;
                    t.IsApplied = TweakManager.IsApplied(kind);
                }
            }
            return all.ToList();
        }

        // ---------- Serviços ----------

        /// <summary>Desabilita serviços (inicialização = desabilitado) e os para.</summary>
        public ActionReport DisableServices(IEnumerable<string> names, IEnumerable<ServiceInfo> services)
        {
            var report = new ActionReport();
            var list = names.ToList();
            if (list.Count == 0) { report.Add("Seleção", "nenhuma alteração", true, "Nenhum item selecionado."); return report; }
            EnsureBaseline();

            foreach (var n in list)
            {
                var svc = services.FirstOrDefault(s => s.Name == n);
                try
                {
                    if (svc is { IsProtected: true })
                    {
                        report.Add(n, "bloqueado", false, "Serviço protegido — desativar pode danificar o Windows.");
                        continue;
                    }
                    if (svc is { StartKind: ServiceStartKind.Disabled } && svc.RunningState == "Parado")
                    {
                        report.Add(n, "já desabilitado", true);
                        continue;
                    }
                    ServiceActions.ChangeStartKind(n, ServiceStartKind.Disabled);
                    report.Add(n, "desabilitado", true);
                    if (svc?.RunningState == "Executando")
                    {
                        try { ServiceActions.Stop(n); report.Add(n, "parado", true); }
                        catch (Exception ex) { report.AddError(n, "parar", ex); }
                    }
                }
                catch (Exception ex) { report.AddError(n, "desabilitar", ex); }
            }
            return report;
        }

        public ActionReport EnableServices(IEnumerable<string> names, ServiceStartKind target)
        {
            var report = new ActionReport();
            var list = names.ToList();
            if (list.Count == 0) { report.Add("Seleção", "nenhuma alteração", true, "Nenhum item selecionado."); return report; }
            EnsureBaseline();
            foreach (var n in list)
            {
                try
                {
                    if (target == ServiceStartKind.Automatic)
                    {
                        ServiceActions.ChangeStartKind(n, ServiceStartKind.Automatic);
                        report.Add(n, "inicialização → automático", true);
                        try { ServiceActions.Start(n); report.Add(n, "iniciado", true); }
                        catch (Exception ex) { report.AddError(n, "iniciar", ex); }
                    }
                    else
                    {
                        ServiceActions.ChangeStartKind(n, target);
                        report.Add(n, $"inicialização → {Label(target)}", true);
                    }
                }
                catch (Exception ex) { report.AddError(n, "habilitar", ex); }
            }
            return report;
        }

        public ActionReport StopServices(IEnumerable<string> names)
        {
            var report = new ActionReport();
            EnsureBaseline();
            foreach (var n in names)
            {
                try { ServiceActions.Stop(n); report.Add(n, "parado", true); }
                catch (Exception ex) { report.AddError(n, "parar", ex); }
            }
            return report;
        }

        public ActionReport StartServices(IEnumerable<string> names)
        {
            var report = new ActionReport();
            EnsureBaseline();
            foreach (var n in names)
            {
                try { ServiceActions.Start(n); report.Add(n, "iniciado", true); }
                catch (Exception ex) { report.AddError(n, "iniciar", ex); }
            }
            return report;
        }

        /// <summary>Aplica o perfil de jogos: desabilita e para todos os serviços classificados como "Recomendado".</summary>
        public ActionReport ApplyGamingPreset(IEnumerable<ServiceInfo> services)
        {
            var report = new ActionReport();
            EnsureBaseline();
            var safe = services.Where(s => !s.IsProtected && s.Risk == RiskLevel.Recommended).ToList();
            if (safe.Count == 0) { report.Add("Perfil de jogos", "nada a fazer", true, "Nenhum serviço recomendado encontrado."); return report; }
            foreach (var s in safe)
            {
                try
                {
                    if (s.StartKind == ServiceStartKind.Disabled && s.RunningState == "Parado")
                    {
                        report.Add(s.Name, "já desabilitado", true);
                        continue;
                    }
                    ServiceActions.ChangeStartKind(s.Name, ServiceStartKind.Disabled);
                    report.Add(s.Name, "desabilitado (perfil de jogos)", true);
                    if (s.RunningState == "Executando")
                    {
                        try { ServiceActions.Stop(s.Name); report.Add(s.Name, "parado", true); }
                        catch (Exception ex) { report.AddError(s.Name, "parar", ex); }
                    }
                }
                catch (Exception ex) { report.AddError(s.Name, "perfil de jogos", ex); }
            }
            return report;
        }

        // ---------- Tarefas agendadas ----------

        public ActionReport SetTasksEnabled(IEnumerable<ScheduledTaskInfo> tasks, bool enabled)
        {
            var report = new ActionReport();
            EnsureBaseline();
            foreach (var t in tasks)
            {
                try
                {
                    TaskActions.SetEnabled(t.FullPath, enabled);
                    report.Add(t.Name, enabled ? "habilitado" : "desabilitado", true);
                }
                catch (Exception ex) { report.AddError(t.Name, "tarefa", ex); }
            }
            return report;
        }

        // ---------- Inicialização ----------

        public ActionReport DisableStartups(IEnumerable<StartupItem> items)
        {
            var report = new ActionReport();
            EnsureBaseline();
            foreach (var it in items)
            {
                try
                {
                    if (it.DisabledByTothBoost) { report.Add(it.Name, "já desabilitado", true); continue; }
                    StartupScanner.Disable(it);
                    report.Add(it.Name, "desabilitado", true);
                }
                catch (Exception ex) { report.AddError(it.Name, "desabilitar", ex); }
            }
            return report;
        }

        public ActionReport EnableStartups(IEnumerable<StartupItem> items)
        {
            var report = new ActionReport();
            EnsureBaseline();
            foreach (var it in items)
            {
                try
                {
                    if (!it.DisabledByTothBoost) { report.Add(it.Name, "já habilitado", true); continue; }
                    StartupScanner.Enable(it);
                    report.Add(it.Name, "habilitado", true);
                }
                catch (Exception ex) { report.AddError(it.Name, "habilitar", ex); }
            }
            return report;
        }

        // ---------- Ajustes ----------

        public ActionReport ApplyTweak(string id)
        {
            var report = new ActionReport();
            EnsureBaseline();
            if (!Enum.TryParse<TweakKind>(id, out var kind)) { report.Add(id, "ajuste", false, "ID desconhecido"); return report; }
            try
            {
                TweakManager.Apply(kind);
                SnapshotService.RecordAppliedTweak(id);
                report.Add(TweakCatalog.Get(id).Name, "aplicado", true);
            }
            catch (Exception ex) { report.AddError(TweakCatalog.Get(id).Name, "aplicar ajuste", ex); }
            return report;
        }

        public ActionReport RevertTweak(string id)
        {
            var report = new ActionReport();
            if (!Enum.TryParse<TweakKind>(id, out var kind)) { report.Add(id, "ajuste", false, "ID desconhecido"); return report; }
            try
            {
                TweakManager.Revert(kind);
                report.Add(TweakCatalog.Get(id).Name, "removido", true);
            }
            catch (Exception ex) { report.AddError(TweakCatalog.Get(id).Name, "remover ajuste", ex); }
            return report;
        }

        // ---------- Restauração ----------

        public ActionReport Restore(string snapshotName) => SnapshotService.Restore(snapshotName);

        public List<SnapshotItem> ListSnapshots() => SnapshotService.List();

        /// <summary>Cria um snapshot manual com o estado ATUAL (nome único por hora).</summary>
        public void CreateSnapshot(string name)
        {
            SnapshotService.Create(name, GetServices(), GetTasks(), new StartupScanner().ScanAll());
        }

        private static string Label(ServiceStartKind k) => k switch
        {
            ServiceStartKind.Boot => "inicialização (boot)",
            ServiceStartKind.System => "sistema",
            ServiceStartKind.Automatic => "automático",
            ServiceStartKind.Manual => "manual",
            ServiceStartKind.Disabled => "desabilitado",
            _ => "?"
        };
    }
}