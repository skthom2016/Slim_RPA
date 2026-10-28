// Global using alias so that all Application-layer files can use IClock without
// a fully-qualified name. IClock lives in the Domain layer per the project standard.
// NOTE: Do NOT add 'global using Tracto.TerminalRuntime.Application.Common' here —
// that creates an ambiguity with Domain.Common.Result/Error in files that also
// import Tracto.TerminalRuntime.Domain. Use explicit per-file using statements instead.
