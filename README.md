# 🚀 TothBoost — Otimizador de Desempenho para Jogos (Windows 10/11)

**TothBoost** é uma aplicação desktop avançada desenvolvida em **C# / .NET 10 (WPF)** focada em otimizar o desempenho do Windows para jogos e tarefas de alta performance, mantendo a estabilidade e a segurança total do sistema operacional através de pontos de restauração e backups automáticos.

---

## 📌 Sumário
- [Recursos Principais](#-recursos-principais)
- [Como Funciona (Arquitetura e Segurança)](#-como-funciona-arquitetura-e-segurança)
- [Como Instalar e Usar (Usuário Final)](#-como-instalar-e-usar-usuário-final)
- [Requisitos do Sistema & Dependências](#-requisitos-do-sistema--dependências)
- [Guia do Desenvolvedor](#-guia-do-desenvolvedor)
  - [Pré-requisitos de Desenvolvimento](#pré-requisitos-de-desenvolvimento)
  - [Estrutura do Projeto](#estrutura-do-projeto)
  - [Comandos Principais (Build & Testes)](#comandos-principais-build--testes)
  - [Adicionando Novos Ajustes ou Regras de Serviços](#adicionando-novos-ajustes-ou-regras-de-serviços)
- [Pipeline de CI/CD](#-pipeline-de-cicd)
- [Licença](#-licença)

---

## ⚡ Recursos Principais

- **⚡ Gerenciador de Processos ao Vivo**: Exibe uso de CPU, memória RAM, leitura/gravação de disco e métricas em tempo real. Permite encerrar e reiniciar processos de forma assíncrona.
- **🛡️ Classificação de Segurança de Serviços**: Separação clara entre serviços de usuário e serviços protegidos do kernel do Windows. Impede que o usuário encerre acidentalmente componentes essenciais do sistema.
- **🚀 Otimização em 1 Clique (Perfil de Jogos)**: Desativa e para automaticamente serviços desnecessários ou secundários durante partidas.
- **📋 Tarefas Agendadas e Inicialização Automática**: Varredura avançada com suporte a quarentena reversível para programas e tarefas de fundo.
- **🛠️ Ajustes Finos de SO (Tweaks)**: Ativação de plano de Alto Desempenho / Desempenho Máximo, remoção de transparências/animações desnecessárias, ajustes de memória e registro.
- **💾 Gerenciador de Perfis Múltiplos**: Salve e alterne perfis de otimização customizados mantendo sempre o perfil `Default` como linha de base intacta.
- **⏪ Pontos de Restauração Integrados**: Criação de ponto de restauração do Windows antes de qualquer modificação ser realizada no computador.

---

## 🛡️ Como Funciona (Arquitetura e Segurança)

O TothBoost opera sob o princípio de **Segurança em Primeiro Lugar**:

1. **Trava de Linha de Base (First-Run Baseline)**: Na primeira inicialização, o TothBoost obrigatoriamente cria um Ponto de Restauração no Windows e grava o perfil `Default` contendo o estado original exato de todos os serviços, tarefas, inicializações e ajustes do sistema.
2. **Knowledge Base (Base de Conhecimento)**: Através do arquivo `knowledge.json` e regras heurísticas no `TothBoost.Core`, a aplicação categoriza serviços em:
   - 🔴 **Protegidos**: Componentes de kernel/drivers do sistema (não modificáveis).
   - 🟢 **Recomendados para Jogos**: Serviços secundários que podem ser desativados sem comprometer a estabilidade.
   - 🟡 **Cautela / Opcionais**: Serviços que dependem do uso específico do usuário (ex: impressão, bluetooth).
3. **Quarentena Reversível**: Programas desativados da inicialização não são excluídos; são movidos para chaves de isolamento e pasta de quarentena do TothBoost (`%ProgramData%\TothBoost\Restauração`), permitindo restauração instantânea.

---

## 📥 Como Instalar e Usar (Usuário Final)

Nenhum instalador complexo é necessário. O TothBoost é distribuído como um executável único e independente (*Single-File Executable*).

1. Acesse as **Releases** no repositório GitHub.
2. Faça o download da versão mais recente do arquivo **`TothBoost.exe`**.
3. Clique com o botão direito sobre o arquivo `TothBoost.exe` e selecione **"Executar como Administrador"** (necessário para gerenciar serviços e criar pontos de restauração do Windows).
4. Ao abrir pela primeira vez, o TothBoost criará a linha de base de segurança. A partir daí, você estará livre para aplicar o **Perfil de Jogos** ou personalizar seus ajustes.

---

## 💻 Requisitos do Sistema & Dependências

### Para o Usuário Final:
- **Sistema Operacional**: Windows 10 (64-bit) ou Windows 11 (64-bit).
- **Privilégios**: Direitos de Administrador no Windows.
- **Dependências**: **NENHUMA**. O executável é 100% autocontido (*self-contained*), incluindo todas as bibliotecas e o runtime do .NET 10 embutidos dentro do único arquivo `.exe`.

---

## 👨💻 Guia do Desenvolvedor

Se você deseja modificar o código-fonte, adicionar novos recursos ou contribuir para o desenvolvimento do TothBoost, siga as instruções abaixo:

### Pré-requisitos de Desenvolvimento
- **SDK**: [.NET 10 SDK](https://dotnet.microsoft.com/download) instalado.
- **IDE**: Visual Studio 2026 / Visual Studio Code (com extensão C# Dev Kit) ou JetBrains Rider.
- **SO de Desenvolvimento**: Windows 10/11.

### Estrutura do Projeto

```text
TothBoost/
├── src/
│   ├── TothBoost.Core/         # Biblioteca base (Scanners, KnowledgeBase, Engine, Snapshots)
│   └── TothBoost.App/          # Interface WPF (.NET 10, XAML, ViewModels, Themes DWM)
├── tests/
│   └── TothBoost.Core.Tests/   # Testes unitários com xUnit
├── .github/workflows/
│   └── ci-cd.yml               # Pipeline automatizada no GitHub Actions
├── publish/                    # Saída de binários publicados
└── TothBoost.slnx              # Solução do projeto (.NET 10 Solution Format)
```

### Comandos Principais (Build & Testes)

Abra o terminal na raiz do repositório:

1. **Restaurar Dependências**:
   ```bash
   dotnet restore
   ```

2. **Compilar a Solução**:
   ```bash
   dotnet build
   ```

3. **Executar a Aplicação localmente**:
   ```bash
   dotnet run --project src/TothBoost.App/TothBoost.App.csproj
   ```

4. **Executar a Suíte de Testes Automated (xUnit)**:
   ```bash
   dotnet test
   ```

5. **Gerar Executável Standalone de Release (`TothBoost.exe`)**:
   ```bash
   dotnet publish src/TothBoost.App/TothBoost.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish/
   ```

### Adicionando Novos Ajustes ou Regras de Serviços

- **Para adicionar um novo ajuste de sistema (Tweak)**:
  1. Adicione a chave correspondente no `TweakKind` em `TothBoost.Core/Models/Models.cs`.
  2. Defina a metadata e descrição do ajuste no `TweakCatalog.cs`.
  3. Implemente a lógica de verificação (`IsApplied`), aplicação (`Apply`) e reversão (`Revert`) no `TweakManager.cs`.
- **Para atualizar regras de classificação de serviços**:
  1. Modifique o arquivo `src/TothBoost.Core/Known/knowledge.json` informando o nome do serviço, categoria, nível de risco (`recommended`, `protected`, `warn`, `optional`) e justificativa.

---

## 🔄 Pipeline de CI/CD

O repositório possui integração contínua e entrega contínua ativas via **GitHub Actions** (`.github/workflows/ci-cd.yml`):
- A cada *Push* ou *Pull Request* na branch `main`, os testes unitários são executados e o binário `TothBoost.exe` é compilado e disponibilizado como artefato de build.
- Ao publicar uma tag de versão (ex: `v1.0.0`), o GitHub Actions cria automaticamente uma **Release no GitHub** anexando o arquivo executável para download direto dos usuários.

---

## 📜 Licença

Desenvolvido por Gabriel Toth. Todos os direitos reservados.
