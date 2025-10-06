# RCA - Incidente Banco de Dados

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
