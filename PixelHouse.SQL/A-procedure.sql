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