# Roadmap multiplataforma — TothBoost

**Status:** o foco atual e oficial e **Windows 10/11 x64**. Este documento avalia
se, quando e como Linux e macOS entram no produto. Ele nao e um compromisso de
entrega: e uma analise de viabilidade para decidir com custo e risco claros.

---

## 1. Resumo executivo

| Plataforma | Build em CI | Valor do produto | Risco de port | Recomendacao |
|---|---|---|---|---|
| **Windows 10/11** | Sim (`windows-latest`) | Alto (foco do produto) | Baixo | **Agora** |
| **Linux** | Sim (`ubuntu-latest`) | Medio-alto (Steam Deck/Proton) | Medio | **Depois, se houver demanda** |
| **macOS** | Sim (`macos-latest`) | Baixo-medio (gaming ainda nichado) | Alto (SIP/TCC, assinatura, sem hardware local) | **Nao vale a pena agora** |

**Conclusao curta:** Linux e tecnicamente viavel e faz sentido como segunda
plataforma. macOS e tecnicamente construivel, mas o retorno nao justifica o
custo agora — especialmente sem um Mac para validar e sem conta Apple Developer.

---

## 2. Por que o TothBoost e Windows-first hoje

O produto nao e um app generico com "if OS": quase todo o valor vem de APIs e
comportamentos exclusivos do Windows.

| Area | Dependencia atual |
|---|---|
| UI .NET | WPF (`net10.0-windows`), XAML, `WindowInteropHelper`, DWM dark mode |
| Servicos | SCM (`sc.exe`, `ServiceController`), chaves `HKLM\SYSTEM\CurrentControlSet\Services` |
| Tweaks | Registro (`HKLM`/`HKCU`), GameDVR, telemetria, hibernacao |
| Processos | Win32 (`OpenProcess`, `K32EmptyWorkingSet`, prioridade, `TerminateProcess`) |
| Agendadas | `schtasks.exe` / Task Scheduler |
| Startup | `Run`/`RunOnce`, pastas Startup, quarentena no registro |
| Rede | `ipconfig /flushdns`, `NetworkThrottlingIndex`, `SystemResponsiveness` |
| Baseline/restore | Pontos de restauracao do Windows + snapshots JSON |
| Nativo Rust | crate `windows`, Win32 (`ShellExecuteW`, `RegOpenKeyExW`, SCM, UI) |
| Elevacao | manifesto `requireAdministrator` + `runas`/token de Administrador |

Parte de `TothBoost.Core` (modelos, relatorios, orquestracao, perfis) e
portavel, mas os scanners e executores sao especificos de Windows.

---

## 3. Arquitetura alvo (pre-requisito para qualquer port)

Antes de escrever codigo para outro SO, separar o que e produto do que e
plataforma:

```
src/
  TothBoost.Core/                 # modelos, relatorios, orquestracao, regras
  TothBoost.Platform.Abstractions # IServiceManager, IProcessManager, ITweakProvider,
                                  # IStartupManager, INetworkManager, ISnapshotStore...
  TothBoost.Platform.Windows      # implementacao atual (movida)
  TothBoost.Platform.Linux        # systemd, /proc, sysctl...
  TothBoost.Platform.MacOS        # launchd, sysctl, pmset...
  TothBoost.App                   # hoje WPF; futuro: Avalonia ou UI por SO
  tothboost-rs/                   # engine nativa, modulos por target
```

Decisoes recomendadas:

1. **Nao portar o WPF.** WPF nunca rodara em Linux/macOS. Para multiplataforma,
   a opcao com melhor custo/beneficio e **Avalonia**. Porem, migrar a UI agora
   so adiciona risco ao Windows; fazer isso apenas quando o backend Linux
   comecar.
2. **CLI/TUI primeiro no Linux.** O motor Rust pode ganhar uma interface de
   linha de comando multiplataforma. Isso entrega valor real (especialmente em
   Steam Deck) sem o custo de portar uma UI grafica inteira.
3. **Elevacao por operacao, nao por aplicativo.** No Windows o app inteiro roda
   elevado. Em Linux/macOS o padrao correto e UI sem root + helper privilegiado
   via `pkexec`/polkit (Linux) ou autorizacao pontual (macOS).
4. **Seguranca e reversibilidade.** Todo tweak novo deve ter deteccao, backup do
   valor original, aplicacao idempotente e reversao testada. Isso vale para
   todas as plataformas.

---

## 4. Linux

### Viabilidade

- **Build/test em CI:** alta. `ubuntu-latest` compila .NET e Rust sem custo
  extra relevante para repositorio publico.
- **Valor para gaming:** medio-alto. Steam Deck/Proton crescendo, mas o usuario
  Linux ja usa ferramentas consolidadas (`gamemode`, `MangoHud`,
  `power-profiles-daemon`, governadores de CPU, `systemd`).
- **Esforco de um MVP:** 6 a 10 semanas para um desenvolvedor, com backend novo
  + CLI; mais 4 a 6 semanas para packaging/hardening.
- **Riscos:** fragmentacao de distros, sistemas imutaveis (SteamOS/Fedora
  Silverblue), polkit, ausencia de "servicos" como no Windows e medicao de
  ganho de FPS que varia por jogo/driver.

### Mapa de funcionalidades

| Windows | Equivalente Linux |
|---|---|
| Servicos (`sc.exe`) | unidades `systemd`, `systemctl disable/enable`, presets |
| Processos e RAM | `/proc`, sinais, cgroups v2, `drop_caches` (com cautela) |
| Startup | arquivos `.desktop` em autostart, `systemd --user` |
| Tarefas agendadas | `systemd` timers / `cron` |
| DNS/rede | `resolvectl flush-caches`, `sysctl net.*`, `tc`/`ethtool` |
| Plano de energia | `power-profiles-daemon`, `cpupower`, `tlp` |
| Tweaks de jogos | `gamemode`, `MangoHud`, `mesa`/`vulkan` env vars |
| Limpeza | `~/.cache`, `/var/tmp`, journal (`journalctl --vacuum-*`) |
| Restore point | backup de configs + `timeshift`/snapshots Btrfs (quando existir) |

### Distribuicoes alvo sugeridas

1. Ubuntu LTS (base de testes e maior publico)
2. SteamOS (maior valor para gaming; requer cuidado com rootfs somente leitura)
3. Fedora (representa o ecossistema RPM)
4. Arch (opcional; valida que nao dependemos de detalhes de uma distro)

### Empacotamento

- `tar.gz` + binario self-contained (mais simples)
- `.deb` e `.rpm`
- AppImage (portavel)
- Flatpak somente se os tweaks de sistema forem explicitamente suportados;
  sandbox conflita com a proposta do app

### Recomendacao Linux

**Vale a pena, mas nao agora.** Fazer um spike tecnico de 1 a 2 semanas quando
houver demanda real (pedidos/usuarios de Steam Deck), comecando por CLI e sem
prometer paridade de features com o Windows.

---

## 5. macOS

### Viabilidade

- **Build/test em CI:** alta. `macos-latest` compila .NET/Avalonia e Rust.
- **Distribuicao:** exige **Apple Developer Program (US$ 99/ano)**, certificado
  Developer ID, hardened runtime e notarizacao (`notarytool`). Sem isso, o
  Gatekeeper bloqueia a experiencia do usuario.
- **Valor para gaming:** baixo-medio. O Mac gaming cresceu com Apple Silicon e
  Game Porting Toolkit, mas o publico que busca "otimizador" e menor que no
  Windows/Linux.
- **Bloqueios tecnicos:** SIP protege `/System`; TCC controla acesso a dados e
  automacao; `launchd` substitui servicos; varias otimizacoes agressivas de
  Windows simplesmente nao existem ou nao sao permitidas.
- **Risco de desenvolver sem Mac:** alto. CI valida build e testes
  automatizados, mas **nao valida UX real, performance, notarizacao local,
  Gatekeeper e comportamento em diferentes versoes**. Seria desenvolvimento
  "as cegas", dependente de beta testers.

### Mapa resumido

| Windows | Equivalente macOS (quando existir) |
|---|---|
| Servicos | `launchd` (agents/daemons), `launchctl` |
| Processos/RAM | `ps`, sinais, `purge` (com cautela) |
| Startup | `SMAppService` / login items |
| Energia | `pmset`, `powermetrics` |
| Rede | `dscacheutil -flushcache`, `sysctl` |
| Tweaks | poucos e limitados por SIP/TCC |
| Restore | snapshots APFS/Time Machine (`tmutil`) |

### Recomendacao macOS

**Nao vale a pena agora.** Sem Mac local e sem assinatura Apple, o custo/risco
e alto e o retorno e incerto. Revisitar apenas se:

- houver demanda pagante comprovada;
- existirem 3 a 5 beta testers com Macs reais;
- a conta Apple Developer e a estrategia de notarizacao estiverem aprovadas.

Se for feito, o caminho e um **spike de 2 semanas** para provar build,
notarizacao e 2 ou 3 tweaks seguros — e so entao decidir por um port completo.

---

## 6. Roadmap faseado

### Fase 0 — Windows (agora)
- CI/CD gera binarios Windows em toda execucao e releases automaticas.
- Testes unitarios/UI + self-test de elevacao e abertura de janela.
- Hardening: logs de crash, benchmark antes/depois, testes de reversao,
  assinatura Authenticode (opcional) para reduzir SmartScreen.
- Meta: Windows 10/11 x64 estavel e confiavel.

### Fase 1 — Preparacao multiplataforma (2 a 3 semanas, quando iniciar)
- Extrair `Platform.Abstractions` e mover o codigo Windows sem mudar
  comportamento.
- Adicionar CI em `ubuntu-latest` e `macos-latest` apenas compilando o Core e
  rodando testes agnosticos (sem UI), para evitar regressao de portabilidade.
- Definir contrato de privilegios (helper elevado) e formato de backup.

### Fase 2 — Linux MVP (6 a 10 semanas)
- Backend Linux: systemd, processos, memoria, rede, startup e tweaks seguros.
- CLI/TUI em Rust + nucleo .NET reutilizado.
- Pacotes `tar.gz`/`.deb`/AppImage; teste em Ubuntu LTS e SteamOS.
- Beta fechado com usuarios de Steam Deck.

### Fase 3 — Linux hardening (4 a 6 semanas)
- Suporte a Fedora/Arch, sistemas imutaveis, rollback robusto.
- UI grafica (Avalonia) somente se a CLI validar demanda.
- Medicoes reproduziveis de ganho e limites documentados.

### Fase 4 — Decisao macOS (2 semanas de spike)
- Provar build, notarizacao, `launchd` e 2 tweaks seguros.
- **Go/no-go** com base em demanda, beta testers e custo de manutencao.
- Se "go": 8 a 12 semanas de port + assinatura/notarizacao continuas.

### Fase 5 — Operacao multiplataforma
- Matriz de CI por SO, artefatos e releases separados por plataforma.
- Auto-update por plataforma; assinatura Windows/macOS; politicas de privacidade.

---

## 7. Impacto no CI/CD

Hoje (correto para o foco atual):

```yaml
jobs:
  test:          # windows-latest
  build-windows: # windows-latest -> artefatos + release
```

Quando o codigo passar a compilar fora do Windows, a evolucao natural e:

```yaml
strategy:
  fail-fast: false
  matrix:
    include:
      - os: windows-latest
        rid: win-x64
      - os: ubuntu-latest
        rid: linux-x64
      - os: macos-latest
        rid: osx-arm64
```

Regras para ativar a matriz:

1. Nao adicionar um SO na matriz antes de o Core compilar e os testes
   agnosticos passarem nele.
2. Releases so sao publicadas apos o self-test/smoke test daquela plataforma.
3. macOS so entra em release com assinatura + notarizacao configuradas.

---

## 8. Custo x beneficio

| Item | Linux | macOS |
|---|---|---|
| Esforco inicial | Medio (6-10 semanas) | Alto (8-12 semanas + spike) |
| Manutencao continua | Media (distros/drivers) | Alta (versoes do macOS, assinatura) |
| Custo financeiro | Baixo (CI gratuito em repo publico) | Apple Developer US$ 99/ano |
| Validacao sem hardware local | Boa (VMs/CI) | Ruim (sem Mac) |
| Valor para o usuario | Medio-alto | Baixo-medio |
| **Decisao** | **Fase futura condicional** | **Adiar / nao fazer agora** |

---

## 9. Gatilhos para revisitar

- **Linux:** 20+ pedidos concretos de usuarios ou tracao com Steam Deck; um
  mantenedor disponivel para o backend Linux; CI e packaging definidos.
- **macOS:** demanda paga, pelo menos 3 beta testers com Mac, conta Apple
  Developer aprovada e spike tecnico bem-sucedido.

Ate esses gatilhos, qualquer esforco em Linux/macOS e melhor investido em
confiabilidade no Windows.
