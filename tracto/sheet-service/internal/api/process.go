package api

import (
	"errors"
	"net/http"

	"tracto/sheet-service/internal/process"
)

// registerProcess adds the routes that run a child job for each pending row of a sheet.
//
//	POST /process/start        begin a batch; replies at once with its id
//	GET  /process/{id}         how far it has got, row by row
//	POST /process/{id}/cancel  stop after cancelling the child it is waiting on
func registerProcess(mux *http.ServeMux, pm *process.Manager) {
	mux.HandleFunc("POST /process/start", func(w http.ResponseWriter, r *http.Request) {
		var req process.StartRequest
		if !decode(w, r, &req) {
			return
		}
		res, err := pm.Start(r.Context(), req)
		if err != nil {
			var busy *process.BusyError
			switch {
			case errors.Is(err, process.ErrInvalid):
				fail(w, http.StatusBadRequest, err)
			case errors.As(err, &busy):
				writeJSON(w, http.StatusConflict, map[string]string{"error": err.Error(), "batchId": busy.BatchID})
			default:
				var src *process.SourceError
				var orch *process.OrchestratorError
				switch {
				case errors.As(err, &src):
					fail(w, statusFor(src.Err), err)
				case errors.As(err, &orch):
					fail(w, http.StatusBadGateway, err)
				default:
					fail(w, http.StatusInternalServerError, err)
				}
			}
			return
		}
		writeJSON(w, http.StatusAccepted, res)
	})

	mux.HandleFunc("GET /process/{id}", func(w http.ResponseWriter, r *http.Request) {
		b, err := pm.Store().Get(r.PathValue("id"))
		switch {
		case errors.Is(err, process.ErrNotFound):
			fail(w, http.StatusNotFound, err)
		case err != nil:
			fail(w, http.StatusInternalServerError, err)
		default:
			writeJSON(w, http.StatusOK, b)
		}
	})

	mux.HandleFunc("POST /process/{id}/cancel", func(w http.ResponseWriter, r *http.Request) {
		id := r.PathValue("id")
		if _, err := pm.Store().Get(id); errors.Is(err, process.ErrNotFound) {
			fail(w, http.StatusNotFound, err)
			return
		}
		if !pm.Cancel(id) {
			fail(w, http.StatusConflict, errors.New("batch is not running"))
			return
		}
		writeJSON(w, http.StatusAccepted, map[string]string{"batchId": id, "state": "Cancelling"})
	})
}
