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
