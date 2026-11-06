import React from "react";
import ReactDOM from "react-dom/client";
import { App } from "./App";
import { FiscalConsultation } from "./components/FiscalConsultation";
import "./styles.css";

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    {window.location.pathname.startsWith("/consulta/dte/") ? (
      <FiscalConsultation code={window.location.pathname.split("/").at(-1)!} />
    ) : (
      <App />
    )}
  </React.StrictMode>,
);
