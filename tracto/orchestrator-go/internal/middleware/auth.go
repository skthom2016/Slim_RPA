package middleware

import (
	"database/sql"
	"log/slog"
	"net"
	"net/http"
	"strings"

	"github.com/golang-jwt/jwt/v5"
)

// JWTAuth protects paths under /jobs, /workers, /admin.
// If signingKey is empty, tokens are parsed (claims extracted) but NOT signature-verified (dev mode).
func JWTAuth(signingKey string, db *sql.DB, logger *slog.Logger) func(http.Handler) http.Handler {
	return func(next http.Handler) http.Handler {
		return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			path := r.URL.Path
			protected := strings.HasPrefix(path, "/jobs") ||
				strings.HasPrefix(path, "/workers") ||
				strings.HasPrefix(path, "/admin")
			if !protected {
				next.ServeHTTP(w, r)
				return
			}

			authHeader := r.Header.Get("Authorization")

			if signingKey == "" {
				// Dev mode: no signing key configured — bypass auth entirely (same as .NET parse-only dev mode)
				next.ServeHTTP(w, r)
				return
			}

			if !strings.HasPrefix(authHeader, "Bearer ") {
				AppendSecurityEvent(db, "AUTH_FAILURE", r.RemoteAddr,
					`{"reason":"missing_bearer","path":"`+path+`"}`)
				jsonError(w, http.StatusUnauthorized, "AUTH_REQUIRED", "Bearer token required")
				return
			}

			tokenStr := strings.TrimPrefix(authHeader, "Bearer ")

			if false {
				// (dev-mode branch removed — handled above)
			} else {
				_, err := jwt.Parse(tokenStr, func(t *jwt.Token) (interface{}, error) {
					if _, ok := t.Method.(*jwt.SigningMethodHMAC); !ok {
						return nil, jwt.ErrSignatureInvalid
					}
					return []byte(signingKey), nil
				}, jwt.WithLeeway(30))
				if err != nil {
					AppendSecurityEvent(db, "AUTH_FAILURE", r.RemoteAddr,
						`{"reason":"invalid_token","path":"`+path+`"}`)
					jsonError(w, http.StatusUnauthorized, "INVALID_TOKEN", "Token validation failed")
					return
				}
			}

			next.ServeHTTP(w, r)
		})
	}
}

// AdminIPAllowlist blocks /admin paths from IPs outside allowedCIDR.
// If allowedCIDR is empty, the middleware is a no-op.
func AdminIPAllowlist(allowedCIDR string, db *sql.DB, logger *slog.Logger) func(http.Handler) http.Handler {
	var _, network, err = net.ParseCIDR(allowedCIDR)
	return func(next http.Handler) http.Handler {
		return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			if allowedCIDR == "" || !strings.HasPrefix(r.URL.Path, "/admin") {
				next.ServeHTTP(w, r)
				return
			}
			if err != nil {
				logger.Error("invalid admin CIDR", "cidr", allowedCIDR, "err", err)
				next.ServeHTTP(w, r)
				return
			}
			host, _, _ := net.SplitHostPort(r.RemoteAddr)
			ip := net.ParseIP(host)
			if ip == nil || !network.Contains(ip) {
				AppendSecurityEvent(db, "ADMIN_IP_BLOCKED", r.RemoteAddr,
					`{"path":"`+r.URL.Path+`"}`)
				jsonError(w, http.StatusForbidden, "IP_BLOCKED", "Client IP not in allowed admin range")
				return
			}
			next.ServeHTTP(w, r)
		})
	}
}
