import React from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import "./styles.css";

const root = document.getElementById("root");
if (!root) throw new Error("找不到 Taskato 前端根节点。");

createRoot(root).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
);
