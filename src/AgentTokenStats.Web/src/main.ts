import { createApp } from "vue";
import { createRouter, createWebHistory } from "vue-router";
import ElementPlus from "element-plus";
import zhCn from "element-plus/es/locale/lang/zh-cn";
import "element-plus/dist/index.css";
import App from "./App.vue";
import HomePage from "./pages/HomePage.vue";
import DashboardLayout from "./pages/DashboardLayout.vue";
import OverviewPage from "./pages/OverviewPage.vue";
import AgentAnalysisPage from "./pages/AgentAnalysisPage.vue";
import TimeAnalysisPage from "./pages/TimeAnalysisPage.vue";
import ModelAnalysisPage from "./pages/ModelAnalysisPage.vue";
import ProjectAnalysisPage from "./pages/ProjectAnalysisPage.vue";
import AboutPage from "./pages/AboutPage.vue";
import "./styles.css";

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: "/", component: HomePage },
    {
      path: "/dashboard",
      component: DashboardLayout,
      redirect: (to) => ({ name: "overview", query: to.query }),
      children: [
        { path: "overview", name: "overview", component: OverviewPage },
        { path: "agents", name: "agents", component: AgentAnalysisPage },
        { path: "time", name: "time", component: TimeAnalysisPage },
        { path: "models", name: "models", component: ModelAnalysisPage },
        { path: "projects", name: "projects", component: ProjectAnalysisPage },
        { path: "sessions", redirect: (to) => ({ name: "projects", query: to.query }) },
        {
          path: ":agentId",
          redirect: (to) => ({
            name: "overview",
            query: { agents: String(to.params.agentId) }
          })
        }
      ]
    },
    { path: "/about", component: AboutPage }
  ]
});

createApp(App).use(router).use(ElementPlus, { locale: zhCn }).mount("#app");
