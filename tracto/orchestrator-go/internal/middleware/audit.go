package middleware

import (
	"bytes"
	"database/sql"
	"encoding/json"
	"fmt"
	"io"
	"log/slog"
	"net/http"
	"regexp"
	"strings"
	"time"

	"github.com/google/uuid"
)

var (
	secretRefPattern = regexp.MustCompile(`(?i)"secretRefs"\s*:\s*\[([^\]]*)\]`)
	passwordPattern  = regexp.MustCompile(`(?i)"password"\s*:\s*"[^"]*"`)
)

type responseWriter struct {
	http.ResponseWriter
	status int
}

func (rw *responseWriter) WriteHeader(code int) {
	rw.status = code
	rw.ResponseWriter.WriteHeader(code)
}

// AuditLogging logs every request + response to both the structured logger and SecurityEvents.
func AuditLogging(db *sql.DB, logger *slog.Logger) func(http.Handler) http.Handler {
	return func(next http.Handler) http.Handler {
		return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			start := time.Now()
			traceID := uuid.NewString()

			var bodySnippet string
			if r.Body != nil && r.ContentLength > 0 && r.ContentLength < 4096 {
				raw, _ := io.ReadAll(r.Body)
				r.Body = io.NopCloser(bytes.NewReader(raw))
				bodySnippet = redactBody(string(raw))
			}

			authHeader := sanitizeAuth(r.Header.Get("Authorization"))

			rw := &responseWriter{ResponseWriter: w, status: 200}
			next.ServeHTTP(rw, r)

			elapsed := time.Since(start)
			logger.Info("request",
				"method", r.Method,
				"path", r.URL.Path,
				"query", r.URL.RawQuery,
				"auth", authHeader,
				"trace", traceID,
				"status", rw.status,
				"ms", elapsed.Milliseconds(),
			)

			details := fmt.Sprintf(`{"method":%q,"path":%q,"auth":%q,"trace":%q,"status":%d,"ms":%d,"body":%q}`,
				r.Method, r.URL.Path, authHeader, traceID, rw.status, elapsed.Milliseconds(), bodySnippet)

			go writeSecurityEvent(db, "AUDIT_LOG", r.RemoteAddr, details)
		})
	}
}

func sanitizeAuth(h string) string {
	if h == "" {
		return ""
	}
	lower := strings.ToLower(h)
	switch {
	case strings.HasPrefix(lower, "bearer "):
		return "Bearer ***"
	case strings.HasPrefix(lower, "basic "):
		return "Basic ***"
	default:
		return "***"
	}
}

func redactBody(body string) string {
	body = secretRefPattern.ReplaceAllStringFunc(body, func(m string) string {
		return `"secretRefs":["***"]`
	})
	body = passwordPattern.ReplaceAllStringFunc(body, func(m string) string {
		return `"password":"***"`
	})
	return body
}

func writeSecurityEvent(db *sql.DB, eventType, sourceIP, details string) {
	if db == nil {
		return
	}
	// truncate details to 4096 chars
	if len(details) > 4096 {
		details = details[:4096]
	}
	id := uuid.NewString()
	now := time.Now().UTC().Format(time.RFC3339Nano)
	db.Exec(
		`INSERT INTO SecurityEvents(Id,EventType,SourceIp,OccurredAt,Details) VALUES(?,?,?,?,?)`,
		id, eventType, sourceIP, now, details,
	)
}

// AppendSecurityEvent is exported so JWT/admin middleware can write security events.
func AppendSecurityEvent(db *sql.DB, eventType, sourceIP, details string) {
	writeSecurityEvent(db, eventType, sourceIP, details)
}

// jsonError writes a standard error JSON body.
func jsonError(w http.ResponseWriter, code int, errCode, msg string) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(code)
	b, _ := json.Marshal(map[string]string{"error": errCode, "message": msg})
	w.Write(b)
}
