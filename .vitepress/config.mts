import { defineConfig } from "vitepress";

// https://vitepress.dev/reference/site-config
export default defineConfig({
  base: "/sts2-tazeu/",
  markdown: {
    math: true,
  },
  themeConfig: {
    // https://vitepress.dev/reference/default-theme-config
    socialLinks: [
      { icon: "github", link: "https://github.com/star-whisper9/sts2-tazeu" },
    ],
  },
  locales: {
    zh: {
      title: "杀戮尖塔 2 - 郊狼相生",
      label: "简体中文",
      lang: "zh-Hans",
      description: "杀戮尖塔 2 郊狼相生(TazeU) Mod 官方文档",
      themeConfig: {
        nav: [
          { text: "首页", link: "/zh/" },
          { text: "使用指南", link: "/zh/usage/" },
          { text: "开发文档", link: "/zh/development/" },
        ],
        sidebar: {
          "/zh/usage/": [
            {
              text: "使用指南",
              items: [
                { text: "快速入门", link: "/zh/usage/" },
                { text: "特点与机制", link: "/zh/usage/features" },
                { text: "进阶配置", link: "/zh/usage/config" },
                { text: "更新日志", link: "/zh/usage/release-notes" },
              ],
            },
          ],
          "/zh/development/": [
            {
              text: "开发文档",
              items: [
                { text: "快速入门", link: "/zh/development/" },
                { text: "项目架构", link: "/zh/development/architecture" },
                { text: "协议详解", link: "/zh/development/protocol" },
                { text: "API 参考", link: "/zh/development/api" },
              ],
            },
          ],
        },
      },
    },
    en: {
      title: "Slay the Spire 2 - TazeU",
      label: "English",
      lang: "en",
      description: "Slay the Spire 2 TazeU Mod Official Documentation",
      themeConfig: {
        nav: [
          { text: "Home", link: "/en/" },
          { text: "Usage", link: "/en/usage/" },
          { text: "Development", link: "/en/development/" },
        ],
        sidebar: {
          "/en/usage/": [
            {
              text: "Usage Guide",
              items: [
                { text: "Quick Start", link: "/en/usage/" },
                { text: "Features & Mechanics", link: "/en/usage/features" },
                { text: "Advanced Config", link: "/en/usage/config" },
                { text: "Release Notes", link: "/en/usage/release-notes" },
              ],
            },
          ],
          "/en/development/": [
            {
              text: "Development",
              items: [{ text: "Dev Guide", link: "/en/development/" }],
            },
          ],
        },
      },
    },
  },
});
