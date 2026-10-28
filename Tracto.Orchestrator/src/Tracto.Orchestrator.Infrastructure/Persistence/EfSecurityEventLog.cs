// EfSecurityEventLog is defined in Security/EfSecurityEventLog.cs (OC-17).
// That implementation uses IDbContextFactory<OrchestratorDbContext> so security events
// are persisted in their own short-lived transaction, independent of the request transaction.
// This file is intentionally left empty to avoid a duplicate class definition.
namespace Tracto.Orchestrator.Infrastructure.Persistence;
