# Compatibilidade do TothBoost com versões do Windows

Este documento separa **suporte oficial**, **compatibilidade best-effort
comprovada em build** e **plataformas não suportadas**. Ele existe para evitar
promessas que o código não consegue cumprir.

## Matriz

| Sistema | App .NET 10 (WPF) | Binário nativo Rust moderno | Binário nativo Rust legado (`TothBoost-Rust-Win7.exe`) |
|---|---|---|---|
| **Windows 11** | ✅ Suporte oficial | ✅ Suporte oficial | ⚠️ Roda, mas não é o alvo recomendado |
| **Windows 10** | ✅ Suporte oficial | ✅ Suporte oficial | ⚠️ Roda, mas não é o alvo recomendado |
| **Windows 8.1** | ❌ Não roda (.NET 10 exige Windows 10+) | ❌ Não garantido | ⚠️ Best-effort, evidência de build |
| **Windows 8** | ❌ Não roda | ❌ Não garantido | ⚠️ Best-effort, evidência de build |
| **Windows 7 SP1 x64** | ❌ Não roda | ❌ Não roda (Rust ≥ 1.78 removeu o suporte) | ⚠️ Best-effort, evidência de build |
| **Windows XP / Vista** | ❌ Não roda | ❌ Não roda | ❌ **Não suportado** |
| **Windows 32-bit (x86)** | ❌ Apenas x64 | ❌ Apenas x64 | ❌ Apenas x64 |

Legenda: ✅ oficialmente suportado e testado em CI · ⚠️ construído e auditado,
mas não executado em uma máquina real dessa versão · ❌ inviável ou fora de escopo.

## Por que o app .NET não roda em 7/8/8.1

O `TothBoost.App` tem como alvo `net10.0-windows` e é publicado como
`win-x64` self-contained. O runtime .NET 10 não suporta Windows 7/8/8.1, então
não existe flag de build que resolva isso. Rodar nessas versões exigiria um
porte separado para .NET Framework 4.8 (ou anterior), com outro projeto e outro
pipeline — não é o mesmo binário.

## Por que o binário Rust moderno não roda em Windows 7

A partir do Rust 1.78, a biblioteca padrão oficial passou a exigir Windows 10
para o target `x86_64-pc-windows-msvc`/`gnu`. O último toolchain com std
funcional em Windows 7 é o **Rust 1.77.0**. Por isso o build legado é feito
separadamente com esse toolchain.

O script `scripts/build-legacy-win7.ps1`:

1. instala/usa `1.77.0-x86_64-pc-windows-gnu`;
2. mantém o `Cargo.lock` na versão 3 (o Cargo 1.77 não lê a versão 4);
3. passa `--major-subsystem-version=6 --minor-subsystem-version=1` ao linker,
   declarando Windows 7 no cabeçalho PE;
4. executa `scripts/check-legacy-pe.ps1`, que lê o PE diretamente e falha se:
   - a versão de subsistema for maior que 6.01;
   - a tabela de imports contiver APIs introduzidas depois do Windows 7
     (por exemplo `GetSystemTimePreciseAsFileTime`, `WaitOnAddress`,
     `SetThreadDescription`, `GetDpiForWindow`, `CreateFile2`);
5. copia o resultado para `dist/legacy/TothBoost-Rust-Win7.exe`.

### Evidência atual (build local)

Auditoria do binário gerado com Rust 1.77.0:

```text
PE AUDIT: src/tothboost-rs/target-win7/release/tothboost.exe
  Machine            : 0x8664 (x86-64)
  Subsystem          : WINDOWS_GUI 6.1
  Imported DLLs      : advapi32.dll, bcrypt.dll, comctl32.dll, gdi32.dll,
                       kernel32.dll, ntdll.dll, oleaut32.dll, shell32.dll,
                       user32.dll, KERNEL32.dll, msvcrt.dll
  Imported functions : 190
  Post-Windows-7 APIs: none
RESULT=PASS build-time Windows 7 compatibility checks
```

O mesmo binário legado também passou no `--self-test` completo em um host
Windows 10 (elevação, janela, 5 abas, tabelas e checkboxes), o que prova que o
build com Rust 1.77 não está quebrado. Isso **não** prova execução no Windows 7.

### Limites dessa evidência

- Não há Windows 7/8/8.1 disponível neste ambiente. A auditoria prova apenas
  **cabeçalho PE + imports**, não o comportamento em tempo de execução.
- **Windows 7 SP1 x64 precisa do KB2533623** (atualização de loader da
  Microsoft). Sem ele, a std do Rust pode falhar ao carregar DLLs.
- O binário legado é o mesmo motor nativo; recursos que dependem de APIs mais
  novas podem degradar silenciosamente. Teste em uma VM antes de uso em produção.

## Por que Windows XP não é suportado

Windows XP não é apenas "uma flag a menos":

- Rust moderno (qualquer versão com std) não gera binários para XP; não há
  target oficial e a std usa APIs de kernel que não existem no XP.
- O app .NET 10 também não roda no XP (nem .NET Framework 4.x sem adaptações).
- O produto usa `SysListView32`, SCM, Task Scheduler, registry e UAC de forma
  assumidamente moderna; UAC nem existe no XP.
- Suportar XP exigiria um motor separado em C/C++ ou Rust `no_std`, com
  camadas próprias de UI, serviços e persistência. Isso é um outro produto, não
  um build do TothBoost atual.

Portanto, **não há nem haverá um `TothBoost-XP.exe` gerado por este repositório**
sem uma reescrita dedicada.

## Como verificar localmente

```powershell
# Testes funcionais do motor nativo
cd src/tothboost-rs
cargo +stable-x86_64-pc-windows-gnu test

# Build legado + auditoria PE
cd ../..
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-legacy-win7.ps1

# Auditoria de um binário já existente
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check-legacy-pe.ps1 `
  -Path src/tothboost-rs/target-win7/release/tothboost.exe
```

O CI executa o build legado junto do build moderno e publica
`TothBoost-Rust-<versão>-windows7-x64-legacy.zip` como artefato e asset de
release.

## Recomendação prática

- **Windows 10/11:** use `TothBoost.exe` (.NET) ou `TothBoost-Rust.exe`.
- **Windows 7 SP1/8/8.1 x64:** use `TothBoost-Rust-Win7.exe`, ciente de que é
  best-effort e não foi validado em máquina real por este projeto.
- **Windows XP/Vista ou 32-bit:** não suportado.
