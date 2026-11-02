package sheet

import "tracto/sheet-service/internal/msauth"

// graphTokens picks the token source from the environment:
//
//	MS_ACCESS_TOKEN                a ready access token, or
//	MS_TENANT_ID + MS_CLIENT_ID    the sign-in saved by "sheet-service login" (SHEET_SERVICE_DB)
//
// The Excel range API accepts signed-in users only, so there is no app-secret login.
func graphTokens(cfg Config) (tokenSource, error) {
	if t := cfg.getenv("MS_ACCESS_TOKEN"); t != "" {
		return staticToken(t), nil
	}
	mc, dbPath, err := msauth.ConfigFromEnv(cfg.getenv)
	if err != nil {
		return nil, err
	}
	mc.LoginBase = cfg.LoginBase
	store, err := msauth.OpenStore(dbPath)
	if err != nil {
		return nil, err
	}
	return msauth.NewSource(store, mc, cfg.Connection), nil
}
