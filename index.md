---
layout: false
---

<script setup>
import { inBrowser } from 'vitepress'

if (inBrowser) {
  // 获取浏览器语言
  const lang = navigator.language || navigator.userLanguage || ''
  const isZh = lang.toLowerCase().includes('zh')
  
  // 判断跳转
  const target = isZh ? '/zh/' : '/en/'
  window.location.replace(target)
}
</script>
