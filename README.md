# README

Este guia documenta a execução das tarefas entregáveis, com um passo a passo para a criação e validação das soluções propostas.

## A) Procedure (SQL Server)

### Scripts Entregáveis

Script para criação da `Stored Procedure LoadFato_PrazosExpedicao`

```sql
/*
------------------------------------------------------------
-- File: A-procedure.sql
-- Author: André Carapiá
-- Create date: 06/10/2025
-- Description:  Cria procedure usp_LoadFato_PrazosExpedicao para carregar
  pedidos cuja primeira expedição ocorreu nos últimos 30 dias.
------------------------------------------------------------
*/

IF OBJECT_ID('dbo.usp_LoadFato_PrazosExpedicao', 'P') IS NOT NULL
    DROP PROCEDURE dbo.usp_LoadFato_PrazosExpedicao;
GO

CREATE PROCEDURE dbo.usp_LoadFato_PrazosExpedicao
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now DATETIME = GETDATE();

    BEGIN TRY
        ;WITH FirstExped AS (
            -- Captura a primeira expedição por pedido
            SELECT
                e.IdPedido,
                MIN(e.DataExpedicao) AS DataExpedicao
            FROM Expedicoes e
            GROUP BY e.IdPedido
        )
        INSERT INTO Fato_PrazosExpedicao
            (IdPedido, DataPedido, DataExpedicao, PrazoDias, StatusPedido, DataCarga)
        SELECT
            p.IdPedido,
            p.DataPedido,
            fe.DataExpedicao,
            DATEDIFF(DAY, p.DataPedido, fe.DataExpedicao) AS PrazoDias,
            p.StatusPedido,
            @Now AS DataCarga
        FROM Pedidos p
        INNER JOIN FirstExped fe ON p.IdPedido = fe.IdPedido
        WHERE fe.DataExpedicao >= DATEADD(DAY, -30, @Now)  -- últimos 30 dias
          AND NOT EXISTS (
              SELECT 1 FROM Fato_PrazosExpedicao f WHERE f.IdPedido = p.IdPedido
          );

    END TRY
    BEGIN CATCH
        DECLARE @ErrMsg NVARCHAR(4000) = ERROR_MESSAGE();
        DECLARE @ErrNum INT = ERROR_NUMBER();
        RAISERROR('usp_LoadFato_PrazosExpedicao failed: %d - %s', 16, 1, @ErrNum, @ErrMsg);
    END CATCH

/*
Notas de performance:
- O uso de CTE + agregação (MIN) garante abordagem set-based eficiente.
- Índices recomendados:
    * Expedicoes(IdPedido, DataExpedicao)
    * Pedidos(IdPedido)
    * Fato_PrazosExpedicao(IdPedido)
- Para grandes volumes, rodar em batches (por data ou ID) para reduzir locks.
*/

END
GO

```

**Script para `CREATE`, `INSERT` e `EXEC` de inserção de dados**

```sql
/*
------------------------------------------------------------
Arquivo: seed_test_data.sql
Autor: André Carapiá
Descrição:
  Cria tabelas de exemplo e popula com dados de teste para
  validar a usp_LoadFato_PrazosExpedicao.
------------------------------------------------------------
*/

-- Limpeza prévia
IF OBJECT_ID('Fato_PrazosExpedicao', 'U') IS NOT NULL DROP TABLE Fato_PrazosExpedicao;
IF OBJECT_ID('Expedicoes', 'U') IS NOT NULL DROP TABLE Expedicoes;
IF OBJECT_ID('Pedidos', 'U') IS NOT NULL DROP TABLE Pedidos;
GO

-- Criação das tabelas
CREATE TABLE Pedidos (
    IdPedido INT PRIMARY KEY,
    DataPedido DATETIME NOT NULL,
    IdCliente INT NOT NULL,
    StatusPedido NVARCHAR(50) NOT NULL
);

CREATE TABLE Expedicoes (
    IdExpedicao INT IDENTITY(1,1) PRIMARY KEY,
    IdPedido INT NOT NULL,
    DataExpedicao DATETIME NOT NULL,
    Transportadora NVARCHAR(100),
    FOREIGN KEY (IdPedido) REFERENCES Pedidos(IdPedido)
);

CREATE TABLE Fato_PrazosExpedicao (
    IdFato INT IDENTITY(1,1) PRIMARY KEY,
    IdPedido INT NOT NULL,
    DataPedido DATETIME NOT NULL,
    DataExpedicao DATETIME NOT NULL,
    PrazoDias INT NOT NULL,
    StatusPedido NVARCHAR(50) NOT NULL,
    DataCarga DATETIME NOT NULL
);
GO

-- População de Pedidos
INSERT INTO Pedidos (IdPedido, DataPedido, IdCliente, StatusPedido)
VALUES
(1, DATEADD(DAY, -40, GETDATE()), 1, 'Entregue'),   -- fora dos 30 dias
(2, DATEADD(DAY, -20, GETDATE()), 2, 'Entregue'),   -- dentro dos 30 dias
(3, DATEADD(DAY, -10, GETDATE()), 3, 'Entregue'),   -- dentro dos 30 dias
(4, DATEADD(DAY, -25, GETDATE()), 4, 'Cancelado'),  -- dentro dos 30 dias
(5, DATEADD(DAY, -2,  GETDATE()), 5, 'Novo'),       -- dentro dos 30 dias
(6, DATEADD(DAY, -5,  GETDATE()), 6, 'Entregue');   -- dentro dos 30 dias
GO

-- População de Expedicoes
INSERT INTO Expedicoes (IdPedido, DataExpedicao)
VALUES
-- Pedido 1: fora do range (primeira expedição há 35 dias)
(1, DATEADD(DAY, -35, GETDATE())),

-- Pedido 2: múltiplas expedições (primeira há 18 dias)
(2, DATEADD(DAY, -18, GETDATE())),
(2, DATEADD(DAY, -10, GETDATE())),

-- Pedido 3: expedição há 8 dias
(3, DATEADD(DAY, -8, GETDATE())),

-- Pedido 4: expedição há 25 dias
(4, DATEADD(DAY, -25, GETDATE())),

-- Pedido 5: expedição há 1 dia
(5, DATEADD(DAY, -1, GETDATE())),

-- Pedido 6: nenhuma expedição (deve ser ignorado)
(6, DATEADD(DAY, -60, GETDATE())); -- simula erro proposital, fora de range
GO

-- Validação inicial
PRINT '>>> Dados de teste criados com sucesso.';
SELECT * FROM Pedidos;
SELECT * FROM Expedicoes;

-- Execução da procedure
EXEC dbo.usp_LoadFato_PrazosExpedicao;
GO

-- Conferência
SELECT * FROM Fato_PrazosExpedicao ORDER BY IdPedido;

-- Reexecução para testar idempotência (não deve inserir novamente)
EXEC dbo.usp_LoadFato_PrazosExpedicao;
SELECT COUNT(*) AS TotalDepoisReexec FROM Fato_PrazosExpedicao;
GO
```

#### 💡 Como usar

1. Rode `A-procedure.sql` para o *CREATE* da procedure no SQL Server Management Studio.
1. Em seguida Execute `seed_test_data.sql`.
1. Veja os resultados no output.
    - Pedidos esperados (inseridos): `2`, `3`, `4`, `5`
    - Ignorados:
        - `1` → fora dos 30 dias
        - `6` → expedição fora do range

## B) C# - Worker

O Worker abaixo foi gerado na versão .NET 8.0, e implementa, abaixo a classe `Program.cs` e `Worker.cs`.
A Solution completa está disponível na pasta `PixelHouse_Teste_Dev_Back/PixelHouse.Worker`.

### Program.cs

```c#
using PixelHouse.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();


var host = builder.Build();
host.Run();
```

### Worker.cs

```c#
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PixelHouse.Worker
{
    public class Worker : BackgroundService
    {
        static readonly List<string> _buffer = new();
        static readonly SemaphoreSlim _semaphore = new(20);
        const string ConnectionString = "Data Source=DESKTOP-AV8CNU7;Initial Catalog=master;Max Pool Size=100;Encrypt=True;TrustServerCertificate=True;Integrated Security=SSPI;";
        const int MaxRetries = 3;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Cria mensagens simuladas
            for (int i = 0; i < 2000; i++)
                _buffer.Add("{ \"orderId\": " + i + " }");

            Log("info", "Worker iniciado", null);

            var tasks = new List<Task>();

            foreach (var msg in _buffer)
            {
                await _semaphore.WaitAsync(stoppingToken);
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await ExecuteWithRetryAsync(async () =>
                        {
                            await ProcessMessageAsync(msg, stoppingToken);
                        });
                    }
                    catch (Exception ex)
                    {
                        Log("error", "Falha ao processar mensagem", new { ex.Message, msg });
                    }
                    finally
                    {
                        _semaphore.Release();
                    }
                }, stoppingToken));
            }

            await Task.WhenAll(tasks);
            Log("info", "Worker finalizado", null);
        }

        static async Task ProcessMessageAsync(string msg, CancellationToken cancellationToken)
        {
            int? orderId = null;

            try
            {
                using var doc = JsonDocument.Parse(msg);
                if (doc.RootElement.TryGetProperty("orderId", out var el))
                    orderId = el.GetInt32();
            }
            catch
            {
                Log("warn", "Mensagem inválida (não é JSON válido)", new { msg });
                return;
            }

            using var con = new SqlConnection(ConnectionString);
            await con.OpenAsync(cancellationToken);

            var ensure = new SqlCommand(@"
IF OBJECT_ID('dbo.Logs', 'U') IS NULL
BEGIN
    CREATE TABLE Logs (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        OrderId INT NULL,
        Payload NVARCHAR(MAX),
        CreatedAt DATETIME DEFAULT GETDATE()
    );
END", con);
            await ensure.ExecuteNonQueryAsync(cancellationToken);

            var cmd = new SqlCommand(@"
IF @orderId IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM Logs WHERE OrderId = @orderId)
        INSERT INTO Logs (OrderId, Payload) VALUES (@orderId, @payload);
END
ELSE
BEGIN
    INSERT INTO Logs (Payload) VALUES (@payload);
END", con);

            cmd.Parameters.AddWithValue("@payload", msg);
            cmd.Parameters.AddWithValue("@orderId", (object?)orderId ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync(cancellationToken);

            Log("info", "Mensagem processada", new { orderId });
        }

        static async Task ExecuteWithRetryAsync(Func<Task> func)
        {
            int attempt = 0;

            while (true)
            {
                try
                {
                    await func();
                    return;
                }
                catch (SqlException ex) when (IsTransient(ex) && attempt < MaxRetries)
                {
                    attempt++;
                    int delay = (int)Math.Pow(2, attempt) * 100;
                    Log("warn", $"Erro transitório (tentativa {attempt}), aguardando {delay}ms", new { ex.Message });
                    await Task.Delay(delay);
                }
                catch (Exception ex) when (attempt < MaxRetries)
                {
                    attempt++;
                    int delay = (int)Math.Pow(2, attempt) * 100;
                    Log("warn", $"Erro (tentativa {attempt}), aguardando {delay}ms", new { ex.Message });
                    await Task.Delay(delay);
                }
                catch (Exception ex)
                {
                    Log("error", "Falha definitiva", new { ex.Message });
                    throw;
                }
            }
        }

        static bool IsTransient(SqlException ex)
        {
            foreach (SqlError err in ex.Errors)
                if (err.Number is -2 or 1205 or 4060 or 10928 or 10929)
                    return true;
            return false;
        }

        static void Log(string level, string message, object? data = null)
        {
            var entry = new
            {
                Timestamp = DateTime.UtcNow,
                Level = level.ToUpper(),
                Message = message,
                Data = data
            };
            Console.WriteLine(JsonSerializer.Serialize(entry));
        }
    }
}

```

## C) RCA - Incidente Banco de Dados

**RCA** — Incidente (Segunda 17/06/2024 16:00 - 18:00)

**Causa provável**: A latência aumentou por bloqueios (PageLatch/Lock waits) provocados por uma consulta de relatório que fez scan/ordenamento de grande volume na tabela de pedidos enquanto diversos UPDATEs (SET Status='SHIPPED') tentavam modificar linhas, gerando contenção de páginas e bloqueios. O padrão indica ausência de índice de cobertura e leitura concorrente em isolamento que gera espera por latch/lock.

**Mitigação imediata**: Pausar o relatório (já testado — latência normaliza) e reexecutar as atualizações. Se necessário, aplicar READ COMMITTED SNAPSHOT temporário para reduzir bloqueios de leitura em linha crítica.

**Prevenção**: Criar índices de cobertura para consultas pesadas, revisar planos de execução, estabelecer janela de execução de relatórios fora do pico e parametrizar limites de concorrência para jobs pesados. Implementar alertas para PageLatch/Lock wait spikes e tempo de resposta de writes.

**Runbook**:

1. Pausar job de relatório;
2. monitorar waits e bloqueios;
3. aplicar índices/atualizar estatísticas em homologação e validar plano;
4. agendar relatório para janela fora de pico e documentar mudanças;
5. comunicar time on-call e fechar post-mortem com lições aprendidas.

## USO DE IA/LLM

Abaixo está a declaração de uso de IA/LLM para este desafio.

## 1) Nível de uso por parte do desafio

- Parte A (SQL): ☐ Não usei IA  ☑ Consultei IA  ☐ Usei IA para gerar parte do código/índices
- Parte B (C#): ☐ Não usei IA  ☑ Consultei IA  ☐ Usei IA para gerar parte do código
- Parte C (RCA): ☐ Não usei IA  ☐ Consultei IA  ☑ Usei IA para redigir parte do texto

## 2) O que a IA produziu (3–6 linhas por parte)

- **A)** Revisão do código, como melhoria de estrutura e elaboração de notas de perfomance
- **B)** Sugestões de melhorias no código, como a implementação de padrões de projeto e boas práticas de programação.
- **C)** Sugestões de melhorias na documentação, como a inclusão de exemplos e a clarificação de instruções.

## 3) Prompts principais (cole abaixo)

### Parte A (SQL)

```md
## Revisar a Procedure (SQL)
> **Prompt:**  
> Eu sou um avaliador técnico. Analise a procedure `usp_LoadFato_PrazosExpedicao` (anexo/cole o código aqui). Faça:  
> 1) resumo do que ela faz (1 linha);  
> 2) liste bugs/riscos/ (race conditions, performance, falha em dados nulos);  
> 3) sugira correções concretas (SQL)
> 4) gere comandos SQL para validar o resultado (selects com valores esperados).  
> **Formato de saída:** pontos 1–4.
```

### Parte B (C#)

```md
## Revisar o Worker (C#) — segurança, concorrência e shutdown
> **Prompt:**  
> Sou um avaliador. Aqui está o arquivo `B-Worker.cs` (cole). Revise como um engenheiro sênior .NET: liste problemas funcionais, riscos de produção e melhorias (max 10 itens). Proponha um patch (diff ou arquivo completo) que implemente: async/await end-to-end, CancellationToken, limite de concorrência, retry/backoff (polly pseudocódigo aceitável) e logs estruturados.
> **Formato de saída:** 1) lista; 2) snippet de patch; 3) xUnit test template; 4) instruções para rodar localmente.
```

### Parte C (RCA)

```md
## Produzir RCA final formatado
> **Prompt:**  
> Com base nos logs e na descrição do incidente (latência de gravação 5ms→120ms; PageLatch/Lock waits; UPDATEs massivos e relatório de leitura), escreva uma RCA de **8–12 linhas** cobrindo: causa provável, mitigação imediata, prevenção e runbook (ação passo-a-passo).
**Resposta esperada:** RCA pronta (8–12 linhas) e runbook enumerado.
```