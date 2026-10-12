// 帮助页：左目录树＋受限宽正文的文档站版式——左栏两级目录（手风琴：当前文章页展开小节、其余收起），
// 点击页节点进文章、小节节点跳 section，主区滚动时当前节高亮跟随；正文阅读宽度 ~760px；
// 窄窗（≤960px，与 styles.css .grid-2 断点一致）左栏收起、page-head 目录按钮展开浮层。
// 主区两种视图：功能索引卡 / 分界面文章。文章小节与界面内「?」文档点共用同一词条（lib/help.ts 单源双视图）；
// 有文档点的小节提供「去界面查看」深链（Shell 以 hash 赋值导航，继承离开守卫）。
// 页集按构建经 lib/help.ts BUILD_HELP_PAGES 过滤（client 五页、server 七页：四共用页复用、settings 不出现、
// devices/packages/pluginPackages 三页仅 server），副标题按构建取双词条。

import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Icon } from '../components/Icon'
import { BUILD_HELP_PAGES } from '../lib/help'
import type { HelpPageId } from '../lib/help'
import { isServerUi } from '../lib/uiMode'
import type { TabId } from '../state/types'

interface HelpProps {
  /** 深链：切到目标界面并弹出对应文档点（Shell 侧以 hash 赋值经路由通道落位，继承离开守卫）。 */
  onOpenInUi: (tab: TabId, anchor: string) => void
}

/* * 窄窗阈值（px）：与 styles.css .grid-2 断点须保持一致——窗口不宽于该值时左栏默认收起。 */
const TOC_NARROW_WIDTH = 960

/** 平滑滚动受 prefers-reduced-motion 约束（jsdom 未实现 matchMedia——typeof 守卫防炸）。 */
function prefersReducedMotion(): boolean {
  return typeof window.matchMedia === 'function' && window.matchMedia('(prefers-reduced-motion: reduce)').matches
}

export function Help({ onOpenInUi }: HelpProps) {
  const { t } = useTranslation('help')
  // 当前构建的帮助页集——构建期折叠出口（形参形态会破坏特征串折叠，生产消费点必须走本常量，见 lib/help.ts 注记）
  const pages = BUILD_HELP_PAGES
  // 页内两级视图：null = 功能索引；否则显示该页文章
  const [article, setArticle] = useState<HelpPageId | null>(null)
  // 滚动高亮：当前阅读位置所在小节 id（仅文章视图有义；目录对应项加 .active / aria-current）
  const [activeTopic, setActiveTopic] = useState<string>('')
  // 窄窗目录收起态（初始读 window.innerWidth 判定；宽窗由 CSS 常驻左栏、按钮隐藏，此态不生效）
  const [tocOpen, setTocOpen] = useState(() => window.innerWidth > TOC_NARROW_WIDTH)
  const mainRef = useRef<HTMLDivElement>(null)
  // 目录小节点击的待跳转小节（跨文章切换时 section 尚未渲染，渲染后 effect 补跳）
  const pendingScrollRef = useRef<string | null>(null)
  const page = pages.find((p) => p.page === article)

  // 待跳转消费：每次渲染后尝试（同页点击经 activeTopic 变更触发渲染，跨页经 article 变更）
  useEffect(() => {
    const id = pendingScrollRef.current
    if (!id) return
    const el = mainRef.current?.querySelector<HTMLElement>(`[data-topic-id="${id}"]`)
    if (el) {
      pendingScrollRef.current = null
      el.scrollIntoView({ behavior: prefersReducedMotion() ? 'auto' : 'smooth', block: 'start' })
    }
  })

  // 文章切换重置高亮（新文章首节由 onScroll 夹逼兜底）；目录小节点击自带高亮（pendingScrollRef 置位），不被本重置清掉
  useEffect(() => {
    if (pendingScrollRef.current) return
    setActiveTopic('')
  }, [article])

  /** 目录小节点击：进对应文章（已在该文章则仅驱动渲染）＋滚动到目标 section。 */
  const goTopic = (pageId: HelpPageId, topicId: string) => {
    pendingScrollRef.current = topicId
    setActiveTopic(topicId)
    setArticle(pageId)
  }

  /** 主区滚动：取各 section 视口顶位置，最后一个 top≤阈值者视为当前节；首尾夹逼（未到首节算首节）。 */
  const onMainScroll = () => {
    if (!page) return
    const sections = Array.from(mainRef.current?.querySelectorAll<HTMLElement>('[data-topic-id]') ?? [])
    if (sections.length === 0) return
    const threshold = (mainRef.current?.getBoundingClientRect().top ?? 0) + 96
    let current = sections[0].dataset.topicId ?? ''
    for (const s of sections) {
      if (s.getBoundingClientRect().top <= threshold) current = s.dataset.topicId ?? current
    }
    setActiveTopic(current)
  }

  const tocNav = (
    <nav className={'help-toc' + (tocOpen ? ' open' : '')} aria-label={t('ui.tocRegion')}>
      <button
        type="button"
        className={'help-toc-item root' + (article === null ? ' active' : '')}
        aria-current={article === null ? 'true' : undefined}
        onClick={() => setArticle(null)}
      >
        {t('ui.tocRoot')}
      </button>
      {pages.map((p) => (
        <div key={p.page} className="help-toc-page">
          <button
            type="button"
            className={'help-toc-item page' + (article === p.page ? ' active' : '')}
            onClick={() => setArticle(p.page)}
          >
            {t(`index.${p.page}.title`)}
          </button>
          {/* 手风琴：仅当前文章页展开小节列表——label 与正文 h3 /「?」气泡同源词条 */}
          {article === p.page && (
            <div className="help-toc-sections">
              {p.topics.map((topic) => (
                <button
                  key={topic.id}
                  type="button"
                  className={'help-toc-item section' + (activeTopic === topic.id ? ' active' : '')}
                  aria-current={activeTopic === topic.id ? 'true' : undefined}
                  onClick={() => goTopic(p.page, topic.id)}
                >
                  {t(`topic.${p.page}.${topic.id}.title`)}
                </button>
              ))}
            </div>
          )}
        </div>
      ))}
    </nav>
  )

  return (
    <div className="page help-page">
      <div className="page-head">
        {/* 窄窗目录切换（宽窗经 CSS 隐藏；aria-expanded 承载收起 / 展开态，无障碍名不随状态闪变） */}
        <button
          type="button"
          className="btn sm ghost help-toc-toggle"
          aria-expanded={tocOpen}
          title={t('ui.tocToggle')}
          aria-label={t('ui.tocToggle')}
          onClick={() => setTocOpen((v) => !v)}
        >
          <Icon name="toc" size={13} />
        </button>
        <div className="page-title">
          {article === null ? (
            <>
              {t('page.title')}
              {/* 副标题按构建切换：client 计数口径串逐字不动，server 独立词条 */}
              <small>{t(isServerUi ? 'page.subtitleServer' : 'page.subtitle')}</small>
            </>
          ) : (
            t(`index.${article}.title`)
          )}
        </div>
        <span className="spacer" />
        {article !== null && (
          <button type="button" className="btn sm" onClick={() => setArticle(null)}>
            <Icon name="back" size={13} />
            {t('ui.backToIndex')}
          </button>
        )}
      </div>
      <div className="help-layout">
        {tocNav}
        <div className="help-main" ref={mainRef} onScroll={onMainScroll}>
          {article === null ? (
            <div className="help-index">
              {pages.map((p) => (
                <button key={p.page} type="button" className="panel help-card" onClick={() => setArticle(p.page)}>
                  <span className="help-card-title">{t(`index.${p.page}.title`)}</span>
                  <span className="help-card-desc">{t(`index.${p.page}.desc`)}</span>
                </button>
              ))}
            </div>
          ) : (
            <div className="help-article">
              <p className="help-article-intro">{t(`article.${article}.intro`)}</p>
              {page?.topics.map((topic) => (
                <section key={topic.id} className="help-topic" data-topic-id={topic.id}>
                  <h3>{t(`topic.${article}.${topic.id}.title`)}</h3>
                  {/* 正文按换行分段：与文档点气泡同一 \n 段落口径 */}
                  {String(t(`topic.${article}.${topic.id}.body`)).split('\n').map((para, i) => (
                    <p key={i}>{para}</p>
                  ))}
                  {topic.anchor && topic.target && (
                    <button type="button" className="btn sm" onClick={() => onOpenInUi(topic.target!, topic.anchor!)}>
                      <Icon name="link" size={13} />
                      {t('ui.openInUi')}
                    </button>
                  )}
                </section>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
