#!/usr/bin/env python3
# -*- coding: utf-8 -*-
from __future__ import annotations
import tkinter as tk
from tkinter import ttk
import os
try:
    # 嵌入右侧解析面板至左侧分页
    from pages.components.right_pane import create_right_pane as create_right_pane_component  # type: ignore
except Exception:
    create_right_pane_component = None  # 回退
try:
    import ttkbootstrap as ttkb  # 颜色友好
except Exception:
    ttkb = None


def _init_button_styles(root: tk.Misc | None = None) -> dict:
    """初始化一套较大尺寸的彩色按钮风格，返回风格名映射。"""
    style = ttk.Style(root)
    # 统一大按钮字体与内边距
    # 调整为适中尺寸（较默认放大，但小于之前的2倍）
    base = dict(font=("Segoe UI", 14, "bold"), padding=(12, 8))
    # 定义自有命名空间，避免与外部样式冲突
    styles = {
        'primary': 'MF.Primary.TButton',
        'success': 'MF.Success.TButton',
        'danger': 'MF.Danger.TButton',
        'warning': 'MF.Warning.TButton',
        'info': 'MF.Info.TButton',
        'secondary': 'MF.Secondary.TButton',
    }
    # 颜色在原生 ttk 下可能不完全生效，但字体/尺寸会生效；ttkbootstrap 下使用主题色
    for key, sname in styles.items():
        style.configure(sname, **base)
    # 背景色（尽力在原生 ttk 下生效；若无效则依赖 ttkbootstrap 或系统主题近似）
    try:
        style.configure('MF.Primary.TButton', foreground='white', background='#0A84FF')
        style.map('MF.Primary.TButton', background=[('active', '#0A6DFF'), ('pressed', '#095EC8')])
        style.configure('MF.Secondary.TButton', foreground='#0A84FF', background='#E6F0FF')
        style.map('MF.Secondary.TButton', background=[('active', '#D9E8FF'), ('pressed', '#C8DCFF')])
    except Exception:
        pass
    # Win11 强调色映射：hover/active/selected 前景变蓝
    try:
        accent = '#0A84FF'
        # 扁平化风格：无描边
        for s in ('MF.Primary.TButton','MF.Secondary.TButton','MF.Success.TButton','MF.Danger.TButton','MF.Warning.TButton','MF.Info.TButton'):
            style.configure(s, relief='flat', borderwidth=0)
        style.map('MF.Primary.TButton', foreground=[('active', accent), ('pressed', accent)])
        style.map('MF.Secondary.TButton', foreground=[('active', accent), ('pressed', accent)])
        style.configure('MF.Success.TButton', background='#22C55E', foreground='white')
        style.map('MF.Success.TButton', background=[('active', '#16A34A'), ('pressed', '#15803D')])
        style.configure('MF.Danger.TButton', background='#EF4444', foreground='white')
        style.map('MF.Danger.TButton', background=[('active', '#DC2626'), ('pressed', '#B91C1C')])
        style.configure('MF.Warning.TButton', background='#F59E0B', foreground='black')
        style.map('MF.Warning.TButton', background=[('active', '#D97706'), ('pressed', '#B45309')])
        style.configure('MF.Info.TButton', background='#38BDF8', foreground='white')
        style.map('MF.Info.TButton', background=[('active', '#0EA5E9'), ('pressed', '#0284C7')])
    except Exception:
        pass
    return styles

def create_playback_controls(controller, parent_left, include_ensemble: bool = True, instrument: str | None = None):
    """左侧：顶部为横向大按钮切换分页，下面为对应内容区域（整页滚动）。
    注意：使用 pack 作为最外层布局，避免与父容器 pack 混用冲突。
    """

    styles = _init_button_styles(getattr(controller, 'root', None))

    # 顶部：横向大按钮作为分页切换
    tabbar = ttk.Frame(parent_left)
    tabbar.pack(side=tk.TOP, fill=tk.X, padx=10, pady=(10, 0))
    # 三列：左右为弹性空白，中间居中放按钮
    try:
        tabbar.grid_columnconfigure(0, weight=1)
        tabbar.grid_columnconfigure(1, weight=0)
        tabbar.grid_columnconfigure(2, weight=1)
    except Exception:
        pass
    center_bar = ttk.Frame(tabbar)
    center_bar.grid(row=0, column=1)
    # 内容容器：放入单一滚动外壳（整页滚动，顶部按钮固定）
    scroll_shell = ttk.Frame(parent_left)
    scroll_shell.pack(side=tk.TOP, fill=tk.BOTH, expand=True)
    vbar = ttk.Scrollbar(scroll_shell, orient=tk.VERTICAL)
    vbar.pack(side=tk.RIGHT, fill=tk.Y)
    canvas = tk.Canvas(scroll_shell, highlightthickness=0, bg='white')
    canvas.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
    canvas.configure(yscrollcommand=vbar.set)
    vbar.configure(command=canvas.yview)
    content_host = ttk.Frame(canvas, padding=(10, 10))
    content_window = canvas.create_window((0, 0), window=content_host, anchor='nw')
    try:
        # 先执行一次布局计算，确保初始宽高非零
        canvas.update_idletasks()
        w = max(1, canvas.winfo_width())
        canvas.itemconfigure(content_window, width=w)
        bbox = canvas.bbox('all')
        if bbox:
            canvas.configure(scrollregion=bbox)
    except Exception:
        pass
    def _on_content_configure(event=None):
        try:
            bbox = canvas.bbox('all')
            if bbox:
                canvas.configure(scrollregion=bbox)
        except Exception:
            pass
    def _on_shell_configure(event=None):
        try:
            w = canvas.winfo_width()
            canvas.itemconfigure(content_window, width=w)
        except Exception:
            pass
    content_host.bind('<Configure>', _on_content_configure)
    canvas.bind('<Configure>', _on_shell_configure)
    try:
        # 立即触发一次宽度同步，避免初次渲染时内容区域宽度为0导致不可见
        if hasattr(controller, 'root') and controller.root:
            controller.root.after(0, _on_shell_configure)
    except Exception:
        pass
    # 鼠标滚轮控制整页滚动（Windows）
    def _on_mouse_wheel(event):
        try:
            delta = int(-1 * (event.delta / 120))
            canvas.yview_scroll(delta, 'units')
        except Exception:
            pass
    canvas.bind_all('<MouseWheel>', _on_mouse_wheel)

    # 定义各分页内容帧（解析/事件/日志 拆分为三个独立分页）
    tab_controls = ttk.Frame(content_host)
    tab_ensemble = ttk.Frame(content_host)
    tab_parse = ttk.Frame(content_host)
    tab_events = ttk.Frame(content_host)
    tab_logs = ttk.Frame(content_host)
    tab_help = ttk.Frame(content_host)

    # 先切换到“控制”以确保初次就有内容显示
    # 实际子内容稍后添加
    try:
        tab_controls.pack(fill=tk.BOTH, expand=True)
    except Exception:
        pass

    # 工具函数：切换显示帧
    def switch_tab(name: str):
        for f in (tab_controls, tab_ensemble, tab_parse, tab_events, tab_logs, tab_help):
            f.pack_forget()
        if name == 'controls':
            tab_controls.pack(fill=tk.BOTH, expand=True)
        elif name == 'ensemble' and include_ensemble:
            tab_ensemble.pack(fill=tk.BOTH, expand=True)
        elif name == 'parse':
            tab_parse.pack(fill=tk.BOTH, expand=True)
        elif name == 'events':
            tab_events.pack(fill=tk.BOTH, expand=True)
        elif name == 'logs':
            tab_logs.pack(fill=tk.BOTH, expand=True)
        elif name == 'help':
            tab_help.pack(fill=tk.BOTH, expand=True)
        # 更新按钮激活样式（简化为 primary 表示激活）
        btn_map = {
            'controls': btn_controls,
            'ensemble': btn_ensemble if include_ensemble else None,
            'parse': btn_parse_btn,
            'events': btn_events_btn,
            'logs': btn_logs_btn,
            'help': btn_help,
        }
        for key, btn in btn_map.items():
            if not btn:
                continue
            btn.configure(style=styles['primary'] if key == name else styles['secondary'])

    # 创建按钮
    btn_controls = ttk.Button(center_bar, text="控制", style=styles['primary'], command=lambda: switch_tab('controls'))
    btn_controls.pack(side=tk.LEFT, padx=(0, 12))
    if include_ensemble:
        btn_ensemble = ttk.Button(center_bar, text="合奏", style=styles['secondary'], command=lambda: switch_tab('ensemble'))
        btn_ensemble.pack(side=tk.LEFT, padx=(0, 12))
    btn_parse_btn = ttk.Button(center_bar, text="解析", style=styles['secondary'], command=lambda: switch_tab('parse'))
    btn_parse_btn.pack(side=tk.LEFT, padx=(0, 12))
    btn_events_btn = ttk.Button(center_bar, text="事件表", style=styles['secondary'], command=lambda: switch_tab('events'))
    btn_events_btn.pack(side=tk.LEFT, padx=(0, 12))
    btn_logs_btn = ttk.Button(center_bar, text="日志", style=styles['secondary'], command=lambda: switch_tab('logs'))
    btn_logs_btn.pack(side=tk.LEFT, padx=(0, 12))
    btn_help = ttk.Button(center_bar, text="帮助", style=styles['secondary'], command=lambda: switch_tab('help'))
    btn_help.pack(side=tk.LEFT, padx=(0, 12))



    # 在“解析/事件/日志”三页中分别嵌入原组件的对应部分
    try:
        from pages.components.right_pane import create_right_pane_component
        if create_right_pane_component:
            # 解析页：仅MIDI解析设置，传递乐器类型
            create_right_pane_component(controller, tab_parse, show_midi_parse=True, show_events=False, show_logs=False, instrument=instrument)
            # 事件表页：仅事件表，传递乐器类型
            create_right_pane_component(controller, tab_events, show_midi_parse=False, show_events=True, show_logs=False, instrument=instrument)
            # 日志页：仅日志
            create_right_pane_component(controller, tab_logs, show_midi_parse=False, show_events=False, show_logs=True, instrument=instrument)
    except Exception:
        pass

    # 统一解析设置（自动移调/手动半音/最短音长阈值）—— 强制绑定到 PlaybackService
    try:
        parse_settings = ttk.LabelFrame(tab_parse, text="解析设置（统一管线）", padding="10")
        parse_settings.pack(side=tk.TOP, fill=tk.X, padx=10, pady=(8, 10))

        # 变量定义并挂到 controller（便于外部读取）
        controller.auto_transpose_enabled_var = tk.BooleanVar(value=True)
        controller.manual_transpose_semi_var = tk.IntVar(value=0)
        controller.min_note_duration_ms_var = tk.IntVar(value=25)

        # 自动移调
        ttk.Checkbutton(parse_settings, text="自动选择白键率最高的整体移调", variable=controller.auto_transpose_enabled_var).grid(row=0, column=0, sticky=tk.W, columnspan=3)

        # 手动移调半音（当自动关闭时才生效）
        ttk.Label(parse_settings, text="手动移调(半音):").grid(row=1, column=0, sticky=tk.W, pady=(6,0))
        spin_manual = ttk.Spinbox(parse_settings, from_=-12, to=12, increment=1, textvariable=controller.manual_transpose_semi_var, width=8)
        spin_manual.grid(row=1, column=1, sticky=tk.W, padx=(6, 16), pady=(6,0))
        # 最短音长阈值
        ttk.Label(parse_settings, text="最短音长阈值(ms): 小于该值的音符将被丢弃").grid(row=1, column=2, sticky=tk.W, pady=(6,0))
        spin_minms = ttk.Spinbox(parse_settings, from_=0, to=2000, increment=10, textvariable=controller.min_note_duration_ms_var, width=8)
        spin_minms.grid(row=1, column=3, sticky=tk.W, padx=(6, 0), pady=(6,0))

        # 白键率显示与计算
        controller.transpose_k_var = tk.StringVar(value="k: 0")
        controller.white_rate_var = tk.StringVar(value="白键率: -")
        k_label = ttk.Label(parse_settings, textvariable=controller.transpose_k_var)
        k_label.grid(row=2, column=0, sticky=tk.W, pady=(8,0))
        rate_label = ttk.Label(parse_settings, textvariable=controller.white_rate_var)
        rate_label.grid(row=2, column=1, sticky=tk.W, pady=(8,0))

        def _refresh_white_rate_from_service():
            try:
                ps = getattr(controller, 'playback_service', None)
                if not ps or not hasattr(ps, 'get_last_analysis_stats'):
                    return
                st = ps.get_last_analysis_stats() or {}
                k = int(st.get('k') or 0)
                rate = st.get('white_rate')
                controller.transpose_k_var.set(f"k: {k:+d}")
                controller.white_rate_var.set(f"白键率: {rate:.3f}" if isinstance(rate, (int, float)) else "白键率: -")
            except Exception:
                pass

        # 计算作业ID用于防抖
        controller._calc_white_rate_job = None

        def _compute_white_rate_now():
            """立即基于当前文件与设置计算白键率（不启动播放）。"""
            try:
                from meowauto.midi import analyzer as _an
                ps = getattr(controller, 'playback_service', None)
                if not ps:
                    return
                # 读取当前文件
                midi_path = getattr(controller, 'midi_path_var', None).get() if hasattr(controller, 'midi_path_var') else ''
                if not midi_path:
                    return
                res = _an.parse_midi(midi_path)
                if not isinstance(res, dict) or not res.get('ok'):
                    return
                notes = res.get('notes') or []
                # 若存在分部选择，则按所选分部进行事件级过滤（不绕过统一流程）
                try:
                    sel = getattr(controller, '_selected_part_names', set()) or set()
                    parts = getattr(controller, '_last_split_parts', {}) or {}
                    if sel and parts and notes:
                        # 收集选中分部的 (track, channel) 组合（program 作为加权条件）
                        sel_keys = set()
                        prog_map = {}
                        for name in sel:
                            sec = parts.get(name)
                            if not sec:
                                continue
                            meta = getattr(sec, 'meta', {}) if hasattr(sec, 'meta') else (sec.get('meta', {}) if isinstance(sec, dict) else {})
                            tr = meta.get('track'); ch = meta.get('channel'); pg = meta.get('program')
                            if tr is None or ch is None:
                                continue
                            sel_keys.add((int(tr), int(ch)))
                            if pg is not None:
                                prog_map[(int(tr), int(ch))] = int(pg)
                        if sel_keys:
                            def _keep(n):
                                try:
                                    t = int(n.get('track'))
                                    c = int(n.get('channel'))
                                    if (t, c) not in sel_keys:
                                        return False
                                    p_need = prog_map.get((t, c))
                                    if p_need is None:
                                        return True
                                    p_has = n.get('program')
                                    return (p_has is None) or int(p_has) == int(p_need)
                                except Exception:
                                    return False
                            filtered = [n for n in notes if _keep(n)]
                            # 若过滤后为空，为避免“无声”误伤，回退为原notes；同时打印提示
                            if filtered:
                                notes = filtered
                                try:
                                    controller._log_message(f"[DEBUG] 分部过滤: 选择{len(sel)}个分部，保留事件 {len(notes)} 条", "DEBUG")
                                except Exception:
                                    pass
                            else:
                                try:
                                    controller._log_message("[DEBUG] 分部过滤结果为空，回退为全曲事件", "WARN")
                                except Exception:
                                    pass
                except Exception:
                    pass
                # 应用当前设置（调用服务内部预处理计算统计）
                if hasattr(ps, '_apply_pre_filters_and_transpose'):
                    processed = ps._apply_pre_filters_and_transpose(notes)  # 计算并更新 last_analysis_stats
                    # 将预处理后的事件缓存到控制器，供“使用已解析结果播放”直接命中
                    try:
                        controller.analysis_notes = list(processed)
                        controller.analysis_file = midi_path
                        controller._log_message(f"[DEBUG] 已缓存解析事件: {len(controller.analysis_notes)} 条", "DEBUG")
                    except Exception:
                        pass
                _refresh_white_rate_from_service()
            except Exception:
                pass

        def _schedule_compute_white_rate(delay_ms: int = 300):
            """防抖调度白键率计算，避免频繁重算。"""
            try:
                if hasattr(controller, 'root') and getattr(controller, 'root', None):
                    if getattr(controller, '_calc_white_rate_job', None):
                        try:
                            controller.root.after_cancel(controller._calc_white_rate_job)
                        except Exception:
                            pass
                    controller._calc_white_rate_job = controller.root.after(delay_ms, _compute_white_rate_now)
                else:
                    _compute_white_rate_now()
            except Exception:
                pass

        # 暴露给 controller，供其他组件安全调用
        try:
            controller._compute_white_rate_now = _compute_white_rate_now
            controller._schedule_compute_white_rate = _schedule_compute_white_rate
        except Exception:
            pass

        ttk.Button(parse_settings, text="计算白键率", command=_compute_white_rate_now).grid(row=2, column=2, sticky=tk.W, pady=(8,0))

        for c in range(4):
            try:
                parse_settings.grid_columnconfigure(c, weight=1)
            except Exception:
                pass

        # 解析引擎选择（自动/pretty_midi/miditoolkit）
        try:
            ttk.Label(parse_settings, text="解析引擎:").grid(row=3, column=0, sticky=tk.W, pady=(8,0))
            controller._engine_label_to_value = {'自动': 'auto', 'pretty_midi': 'pretty_midi', 'miditoolkit': 'miditoolkit'}
            controller._engine_value_to_label = {v: k for k, v in controller._engine_label_to_value.items()}
            controller.parser_engine_var = tk.StringVar(value='pretty_midi')
            engine_combo = ttk.Combobox(parse_settings, textvariable=controller.parser_engine_var, state='readonly', width=14,
                                        values=list(controller._engine_label_to_value.keys()))
            engine_combo.grid(row=3, column=1, sticky=tk.W)

            def _on_engine_changed(*args):
                try:
                    from meowauto.midi import analyzer as _an
                    label = controller.parser_engine_var.get()
                    value = controller._engine_label_to_value.get(label, 'miditoolkit')
                    if hasattr(_an, 'set_default_engine'):
                        _an.set_default_engine(value)
                    # 记录日志
                    try:
                        controller._log_message(f"解析引擎已切换为: {label} ({value})", "INFO")
                    except Exception:
                        pass
                    # 引擎改变后，重新计算白键率
                    try:
                        (getattr(controller, '_schedule_compute_white_rate', None) or _schedule_compute_white_rate)(100)
                    except Exception:
                        _schedule_compute_white_rate(100)
                except Exception:
                    pass

            controller.parser_engine_var.trace_add('write', _on_engine_changed)
            # 初始化执行一次，确保默认引擎生效
            _on_engine_changed()
        except Exception:
            pass

        def _sync_analysis_settings(*args, **kwargs):
            try:
                ps = getattr(controller, 'playback_service', None)
                if ps and hasattr(ps, 'configure_analysis_settings'):
                    ps.configure_analysis_settings(
                        auto_transpose=bool(controller.auto_transpose_enabled_var.get()),
                        manual_semitones=int(controller.manual_transpose_semi_var.get()),
                        min_note_duration_ms=int(controller.min_note_duration_ms_var.get()),
                    )
                # 自动模式下禁用手动输入
                try:
                    if bool(controller.auto_transpose_enabled_var.get()):
                        spin_manual.configure(state='disabled')
                    else:
                        spin_manual.configure(state='normal')
                except Exception:
                    pass
                # 自动触发一次“白键率计算”（防抖），做到每次设置变更即计算并刷新显示
                try:
                    (getattr(controller, '_schedule_compute_white_rate', None) or _schedule_compute_white_rate)(300)
                except Exception:
                    _schedule_compute_white_rate(300)
            except Exception:
                pass

        # 绑定变化事件（即改即生效）
        try:
            controller.auto_transpose_enabled_var.trace_add('write', lambda *a, **k: _sync_analysis_settings())
            controller.manual_transpose_semi_var.trace_add('write', lambda *a, **k: _sync_analysis_settings())
            controller.min_note_duration_ms_var.trace_add('write', lambda *a, **k: _sync_analysis_settings())
        except Exception:
            pass

        # 初始化一次
        _sync_analysis_settings()

        # 若有当前文件路径变量，变化时也自动重新计算白键率
        try:
            # 定义控制器级别的钩子，供 file_select 等组件复用
            def _on_midi_path_changed(*a, **k):
                try:
                    path = controller.midi_path_var.get() if hasattr(controller, 'midi_path_var') else ''
                except Exception:
                    path = ''
                try:
                    controller._log_message(f"[DEBUG] 文件路径变更: {path}", "DEBUG")
                except Exception:
                    pass
                # 立即计算一次，确保UI即时显示白键率/k
                try:
                    (getattr(controller, '_compute_white_rate_now', None) or _compute_white_rate_now)()
                except Exception:
                    _compute_white_rate_now()
                # 再调度一次，兜底刷新
                try:
                    (getattr(controller, '_schedule_compute_white_rate', None) or _schedule_compute_white_rate)(200)
                except Exception:
                    _schedule_compute_white_rate(200)
                # 新增：当文件路径变更时，依据当前乐器自动识别并勾选分部并立即解析（便于导入即用）
                try:
                    # 架子鼓不需要分部识别
                    cur_inst = str(getattr(controller, 'current_instrument', '') or '')
                    if cur_inst != '架子鼓':
                        # 识别并在左侧树中展示分部，同时根据模式/乐器自动勾选
                        if hasattr(controller, '_ui_select_partitions'):
                            controller._ui_select_partitions()
                            # 不在此处自动“应用并解析”，仅完成勾选，保持与用户操作一致
                            # 若完全无勾选，兜底一次全选，避免后续流程无选择
                            try:
                                sels = controller._get_parts_checked_names() if hasattr(controller, '_get_parts_checked_names') else []
                            except Exception:
                                sels = []
                            if not sels and hasattr(controller, '_ui_parts_select_all'):
                                controller._ui_parts_select_all()
                                try:
                                    controller._log_message("[DEBUG] 自动勾选兜底为全选", "DEBUG")
                                except Exception:
                                    pass
                            # 立即应用并解析（异步调度避免UI阻塞）
                            try:
                                if hasattr(controller, 'root') and hasattr(controller, '_ui_apply_selected_parts_and_analyze'):
                                    controller.root.after(10, controller._ui_apply_selected_parts_and_analyze)
                                elif hasattr(controller, '_ui_apply_selected_parts_and_analyze'):
                                    controller._ui_apply_selected_parts_and_analyze()
                            except Exception:
                                pass
                            # 再次异步触发一次“仅分部识别与勾选”，用于 UI 树控件在稍后渲染完成后与后台选择同步
                            try:
                                if hasattr(controller, 'root') and hasattr(controller, '_ui_select_partitions'):
                                    controller.root.after(250, controller._ui_select_partitions)
                            except Exception:
                                pass
                except Exception:
                    pass

            # 绑定到 controller，便于其他文件调用
            controller._on_midi_path_changed = _on_midi_path_changed

            # 若变量已存在，直接注册 trace；如果稍后创建，file_select 中会调用该钩子
            if hasattr(controller, 'midi_path_var') and controller.midi_path_var:
                controller.midi_path_var.trace_add('write', _on_midi_path_changed)
        except Exception:
            pass

        # —— 扫弦/聚合设置 ——
        try:
            strum_frame = ttk.LabelFrame(tab_parse, text="扫弦/聚合设置", padding="10")
            strum_frame.pack(side=tk.TOP, fill=tk.X, padx=10, pady=(0, 10))
            ttk.Label(strum_frame, text="扫弦模式:").grid(row=0, column=0, sticky=tk.W)
            # 中文标签与内部值映射
            controller._strum_label_to_value = {'合并': 'merge', '扫弦': 'arpeggio', '原始': 'original'}
            controller._strum_value_to_label = {v: k for k, v in controller._strum_label_to_value.items()}
            controller.multi_key_cluster_mode_var = tk.StringVar(value='合并')
            mode_combo = ttk.Combobox(strum_frame, textvariable=controller.multi_key_cluster_mode_var, state='readonly', width=10,
                                      values=list(controller._strum_label_to_value.keys()))
            mode_combo.grid(row=0, column=1, sticky=tk.W, padx=(6, 16))
            ttk.Label(strum_frame, text="聚合窗口(ms):").grid(row=0, column=2, sticky=tk.W)
            controller.multi_key_cluster_window_ms_var = tk.IntVar(value=240)
            ttk.Spinbox(strum_frame, from_=0, to=500, increment=1, textvariable=controller.multi_key_cluster_window_ms_var, width=8).grid(row=0, column=3, sticky=tk.W, padx=(6,0))

            def _sync_strum_settings(*args, **kwargs):
                try:
                    ps = getattr(controller, 'playback_service', None)
                    if ps and hasattr(ps, 'configure_auto_player'):
                        label = controller.multi_key_cluster_mode_var.get()
                        mode_val = controller._strum_label_to_value.get(label, 'merge')
                        ps.configure_auto_player(options=dict(
                            multi_key_cluster_mode=mode_val,
                            multi_key_cluster_window_ms=int(controller.multi_key_cluster_window_ms_var.get()),
                        ))
                except Exception:
                    pass
            controller.multi_key_cluster_mode_var.trace_add('write', lambda *a, **k: _sync_strum_settings())
            controller.multi_key_cluster_window_ms_var.trace_add('write', lambda *a, **k: _sync_strum_settings())
            _sync_strum_settings()
        except Exception:
            pass

        # 架子鼓禁用解析设置（直通）
        try:
            if (instrument or '').strip() == '架子鼓':
                for w in parse_settings.winfo_children():
                    try:
                        w.configure(state='disabled')
                    except Exception:
                        pass
        except Exception:
            pass
    except Exception as e:
        # 添加调试信息
        if hasattr(controller, '_log_message'):
            controller._log_message(f"主要演奏控制区域创建失败: {e}", "ERROR")
        else:
            print(f"主要演奏控制区域创建失败: {e}")
        import traceback
        traceback.print_exc()

    # 默认显示“控制”页（再次调用确保初始渲染）
    switch_tab('controls')
    try:
        # 记录一次可见性统计，辅助排查
        cnt = len(tab_controls.winfo_children())
        print(f"DEBUG: tab_controls子控件个数: {cnt}")
        for i, child in enumerate(tab_controls.winfo_children()):
            print(f"DEBUG: tab_controls子控件 {i}: {type(child).__name__}")
        controller._log_message(f"控制分页子控件个数: {cnt}", "INFO")
    except Exception:
        pass

    # —— 全局热键：仅保留 Ctrl+Shift+C 停止所有播放 ——
    try:
        if hasattr(controller, 'root') and controller.root:
            # 解绑可能存在的旧热键（容错处理，不抛错）
            for seq in ('<space>', '<Space>', '<Escape>', '<ESC>', '<Control-s>', '<Control-S>'):
                try:
                    controller.root.unbind_all(seq)
                except Exception:
                    pass
            # 绑定 Ctrl+Shift+C 停止
            def _stop_all_hotkey(event=None):
                try:
                    if hasattr(controller, '_stop_playback'):
                        controller._stop_playback()
                except Exception:
                    pass
            controller.root.bind_all('<Control-Shift-C>', _stop_all_hotkey)
            controller._log_message('已注册热键 Ctrl+Shift+C 用于停止所有播放（已移除其他热键）', 'INFO')
    except Exception:
        pass

    # 帮助页签内容
    help_inner = ttk.Frame(tab_help)
    help_inner.pack(fill=tk.BOTH, expand=True, padx=12, pady=12)
    help_text = (
        "热键说明:\n"
        "• Ctrl+Shift+C: 停止所有播放\n"
        "\n使用说明:\n"
        "1. 选择音频文件 → 音频转MIDI\n"
        "2. 选择MIDI → 解析/事件/日志 中查看与设置\n"
        "3. 设置演奏参数 → 控制 中操作\n"
    )
    ttk.Label(help_inner, text=help_text, justify=tk.LEFT, wraplength=520).pack(anchor=tk.W)

    # 控制页：直接使用容器帧（不使用内层滚动条），避免双滚动条
    ctrl_inner = ttk.Frame(tab_controls)
    ctrl_inner.pack(fill=tk.BOTH, expand=True)
    print("DEBUG: ctrl_inner创建成功")

    # 顶部当前乐器提示（仅提示，不改变布局逻辑）
    try:
        if instrument:
            banner = ttk.Label(ctrl_inner, text=f"当前乐器: {instrument}", foreground="#0A84FF")
            banner.pack(side=tk.TOP, anchor=tk.W, padx=10, pady=(8, 0))
            # 将当前乐器注入控制器桥接，便于定时计划读取
            try:
                if hasattr(controller, 'playback_controller') and controller.playback_controller:
                    controller.playback_controller.current_instrument = instrument
            except Exception:
                pass
    except Exception:
        pass

    # 文件选择已合并到操作控制区域，移除单独的文件选择组件

    # 主要演奏控制区域（文件选择下方，居中显示）
    try:
        print("DEBUG: 开始创建主要演奏控制区域")
        main_control_frame = ttk.LabelFrame(ctrl_inner, text="操作", padding="15")
        main_control_frame.pack(side=tk.TOP, fill=tk.X, padx=10, pady=(0, 10))
        print("DEBUG: 主控制框架创建成功")
        
        # 创建居中容器
        control_center = ttk.Frame(main_control_frame)
        control_center.pack(expand=True, fill=tk.X)
        
        # 文件选择区域（合并到操作控制中）
        file_frame = ttk.Frame(control_center)
        file_frame.pack(side=tk.TOP, fill=tk.X, pady=(0, 10))
        
        # 确保midi_path_var存在（使用主应用程序的共享变量）
        if not hasattr(controller, 'midi_path_var'):
            controller.midi_path_var = tk.StringVar(value="")
        
        # 文件路径输入
        path_entry = ttk.Entry(file_frame, textvariable=controller.midi_path_var, width=30)
        path_entry.pack(side=tk.LEFT, fill=tk.X, expand=True, padx=(0, 5))
        
        # 浏览文件按钮
        def _browse_and_add():
            try:
                from tkinter import filedialog
                file_path = filedialog.askopenfilename(
                    title="选择MIDI文件",
                    filetypes=[("MIDI文件", "*.mid *.midi"), ("所有文件", "*.*")]
                )
                if file_path:
                    # 添加到演奏列表
                    if hasattr(controller, 'playlist') and controller.playlist:
                        if controller.playlist.add_item(file_path):
                            controller._append_playlist_tree_row(file_path)
                    
                    # 自动加载文件到主页面
                    controller.midi_path_var.set(file_path)
                    
                    # 更新文件信息显示
                    if hasattr(controller, '_update_file_info_display'):
                        controller._update_file_info_display(file_path)
                    
                    # 检查当前页面是否为架子鼓页面
                    current_page = getattr(controller, 'current_page', None)
                    if current_page and hasattr(current_page, '_load_midi_from_playlist'):
                        # 架子鼓页面：使用架子鼓专属的加载方法
                        success = current_page._load_midi_from_playlist(file_path)
                        if success:
                            controller._log_message(f"已加载文件到架子鼓页面: {os.path.basename(file_path)}", "SUCCESS")
                        else:
                            controller._log_message("加载文件到架子鼓页面失败", "ERROR")
                    else:
                        # 其他页面：使用通用的加载方法
                        # controller.playback_mode.set("midi")  # 变量不存在，已注释
                        
                        # 解析MIDI文件
                        try:
                            if hasattr(controller, '_analyze_current_midi'):
                                controller._analyze_current_midi()
                                controller._log_message(f"已加载并解析文件到主页面: {os.path.basename(file_path)}", "SUCCESS")
                        except Exception as e:
                            controller._log_message(f"解析失败: {e}", "ERROR")
                            
            except Exception as e:
                if hasattr(controller, '_log_message'):
                    controller._log_message(f"浏览文件失败: {e}", "ERROR")
        
        ttk.Button(file_frame, text="浏览", command=_browse_and_add, width=8).pack(side=tk.RIGHT)
        
        # 文件信息显示
        if not hasattr(controller, 'file_info_var'):
            controller.file_info_var = tk.StringVar(value="未选择文件")
        
        info_label = ttk.Label(control_center, textvariable=controller.file_info_var, 
                              foreground="#666666", font=("TkDefaultFont", 9), anchor="w")
        info_label.pack(side=tk.TOP, fill=tk.X, pady=(0, 10))
        
        # 主要演奏控制按钮（合并开始/停止演奏）
        main_buttons_row = ttk.Frame(control_center)
        main_buttons_row.pack(side=tk.TOP, pady=(0, 8))
        
        # 演奏控制按钮（合并开始/停止演奏）
        def _toggle_auto_play():
            try:
                if hasattr(controller, 'auto_play_button'):
                    current_text = controller.auto_play_button.cget("text")
                    if current_text == "开始演奏":
                        controller._start_auto_play()
                    elif current_text == "停止演奏":
                        controller._stop_auto_play()
            except Exception as e:
                controller._log_message(f"切换演奏状态失败: {e}", "ERROR")
        
        controller.auto_play_button = ttk.Button(main_buttons_row, text="开始演奏", style=styles['success'], 
                                                command=_toggle_auto_play,
                                                width=12)
        controller.auto_play_button.pack(side=tk.LEFT, padx=(0, 12))
        
        # 暂停/恢复按钮（次要操作）
        controller.pause_button = ttk.Button(main_buttons_row, text="暂停", style=styles['warning'], 
                                            command=getattr(controller, '_pause_or_resume', lambda: None),
                                            width=10, state="disabled")
        controller.pause_button.pack(side=tk.LEFT, padx=(0, 12))
        
        # 倒计时显示标签（在按钮右侧显示倒计时状态）
        controller.countdown_label = ttk.Label(main_buttons_row, text="", foreground="#FF6B35", font=('TkDefaultFont', 10, 'bold'))
        controller.countdown_label.pack(side=tk.LEFT, padx=(12, 0))
        
        # 倒计时设置（在主要按钮下方）
        countdown_row = ttk.Frame(control_center)
        countdown_row.pack(side=tk.TOP, pady=(8, 8))
        
        ttk.Label(countdown_row, text="开始前倒计时:").pack(side=tk.LEFT, padx=(0, 6))
        controller.enable_auto_countdown_var = tk.BooleanVar(value=True)
        ttk.Checkbutton(countdown_row, variable=controller.enable_auto_countdown_var).pack(side=tk.LEFT, padx=(0, 6))
        controller.auto_countdown_seconds_var = tk.IntVar(value=3)
        ttk.Spinbox(countdown_row, from_=0, to=30, increment=1, width=6, textvariable=controller.auto_countdown_seconds_var).pack(side=tk.LEFT, padx=(0, 6))
        ttk.Label(countdown_row, text="秒").pack(side=tk.LEFT, padx=(0, 0))
        
        # 演奏列表操作按钮行
        playlist_buttons_row = ttk.Frame(control_center)
        playlist_buttons_row.pack(side=tk.TOP, pady=(8, 8))
        
        # 演奏列表按钮（文件选择已合并到上方，这里只保留管理功能）
        ttk.Button(playlist_buttons_row, text="导入文件夹", style=styles['info'], 
                  command=getattr(controller, '_import_folder_to_playlist', lambda: None),
                  width=10).pack(side=tk.LEFT, padx=(0, 6))
        ttk.Button(playlist_buttons_row, text="移除", style=styles['warning'], 
                  command=getattr(controller, '_remove_from_playlist', lambda: None),
                  width=6).pack(side=tk.LEFT, padx=(0, 6))
        ttk.Button(playlist_buttons_row, text="清空", style=styles['danger'], 
                  command=getattr(controller, '_clear_playlist', lambda: None),
                  width=6).pack(side=tk.LEFT, padx=(0, 6))
        
        # 移除重复的计划按钮，这些功能在定时功能中已有合并按钮
        
        # MIDI音频播放按钮行（合并播放/停止音频）
        midi_buttons_row = ttk.Frame(control_center)
        midi_buttons_row.pack(side=tk.TOP, pady=(8, 8))
        
        def _toggle_midi_playback():
            try:
                if hasattr(controller, 'midi_play_button'):
                    current_text = controller.midi_play_button.cget("text")
                    if current_text == "播放MIDI音频":
                        controller._play_midi()
                        # 更新按钮状态
                        controller.midi_play_button.configure(text="停止音频", style=styles['danger'])
                    elif current_text == "停止音频":
                        controller._stop_playback()
                        # 更新按钮状态
                        controller.midi_play_button.configure(text="播放MIDI音频", style=styles['info'])
            except Exception as e:
                controller._log_message(f"切换MIDI播放状态失败: {e}", "ERROR")
        
        controller.midi_play_button = ttk.Button(midi_buttons_row, text="播放MIDI音频", style=styles['info'], 
                                                command=_toggle_midi_playback, width=12)
        controller.midi_play_button.pack(side=tk.LEFT, padx=(0, 0))
        
        # 快捷键提示
        hint = ttk.Label(control_center, text="快捷键: Ctrl+Shift+C=停止所有播放", foreground="#666")
        hint.pack(side=tk.TOP, pady=(8, 0), anchor=tk.W)
        
        # ===== 全局播放事件订阅：自动刷新按钮状态 =====
        try:
            bus = getattr(controller, 'event_bus', None)
            if bus:
                controller._playback_evt_tokens = getattr(controller, '_playback_evt_tokens', [])

                def _ui_apply(fn):
                    try:
                        if hasattr(controller, 'root') and controller.root:
                            controller.root.after(0, fn)
                        else:
                            fn()
                    except Exception:
                        pass

                def _on_started(_payload=None):
                    def _apply():
                        try:
                            if hasattr(controller, 'auto_play_button'):
                                controller.auto_play_button.configure(text="停止演奏", command=getattr(controller, '_stop_auto_play', lambda: None))
                            if hasattr(controller, 'pause_button'):
                                controller.pause_button.configure(text="暂停", state="normal")
                        except Exception:
                            pass
                    _ui_apply(_apply)

                def _on_stopped(_payload=None):
                    def _apply():
                        try:
                            if hasattr(controller, 'auto_play_button'):
                                controller.auto_play_button.configure(text="开始演奏", command=getattr(controller, '_start_auto_play', lambda: None))
                            if hasattr(controller, 'pause_button'):
                                controller.pause_button.configure(text="暂停", state="disabled")
                        except Exception:
                            pass
                    _ui_apply(_apply)

                def _on_paused(_payload=None):
                    def _apply():
                        try:
                            if hasattr(controller, 'pause_button'):
                                controller.pause_button.configure(text="恢复", state="normal")
                        except Exception:
                            pass
                    _ui_apply(_apply)

                def _on_resumed(_payload=None):
                    def _apply():
                        try:
                            if hasattr(controller, 'pause_button'):
                                controller.pause_button.configure(text="暂停", state="normal")
                        except Exception:
                            pass
                    _ui_apply(_apply)

                def _on_complete(_payload=None):
                    _on_stopped(_payload)

                try:
                    controller._playback_evt_tokens.append(bus.subscribe('playback.start', _on_started))
                except Exception:
                    pass
                try:
                    controller._playback_evt_tokens.append(bus.subscribe('playback.stop', _on_stopped))
                    controller._playback_evt_tokens.append(bus.subscribe('playback.pause', _on_paused))
                    controller._playback_evt_tokens.append(bus.subscribe('playback.resume', _on_resumed))
                    controller._playback_evt_tokens.append(bus.subscribe('playback.complete', _on_complete))
                except Exception:
                    pass
        except Exception:
            pass
        
    except Exception as e:
        try:
            controller._log_message(f"主要演奏控制区域创建失败: {e}", "ERROR")
            import traceback
            controller._log_message(f"详细错误: {traceback.format_exc()}", "ERROR")
        except Exception:
            import traceback
            print(f"主要演奏控制区域创建失败: {e}")
            print(f"详细错误: {traceback.format_exc()}")

    # 控制页（根据乐器类型提供专属设置）
    try:
        # 电子琴和吉他：显示通用演奏模式（已移至主要演奏控制区域）
        if (instrument or '').strip() in ('电子琴', '吉他', ''):
            # 演奏模式已在主要演奏控制区域设置，此处跳过
            pass
        
        # 贝斯：简化设置，无和弦功能
        elif (instrument or '').strip() == '贝斯':
            bass_info = ttk.LabelFrame(ctrl_inner, text="贝斯设置", padding="8")
            bass_info.pack(side=tk.TOP, fill=tk.X, padx=10, pady=(4, 8))
            ttk.Label(bass_info, text="键位：3×7布局（与钢琴上三行一致）", foreground="#666").pack(anchor=tk.W)
            ttk.Label(bass_info, text="注意：贝斯无和弦功能，解析时将禁用和弦相关选项", foreground="#B00020").pack(anchor=tk.W)
        
        # 架子鼓：专属设置（直接播放，不走解析流程）
        elif (instrument or '').strip() == '架子鼓':
            drums_info = ttk.LabelFrame(ctrl_inner, text="架子鼓设置", padding="8")
            drums_info.pack(side=tk.TOP, fill=tk.X, padx=10, pady=(4, 8))
            ttk.Label(drums_info, text="键位：专用鼓键映射（1QW2345TER等）", foreground="#666").pack(anchor=tk.W)
            ttk.Label(drums_info, text="模式：直接播放鼓MIDI，无需解析设置", foreground="#0A84FF").pack(anchor=tk.W)
            ttk.Label(drums_info, text="注意：架子鼓不使用解析栏设置，直接读取MIDI文件播放", foreground="#B00020").pack(anchor=tk.W)

        # 速度（保留倍速控件）
        av_frame = ttk.LabelFrame(ctrl_inner, text="速度设置", padding="10")
        av_frame.pack(side=tk.TOP, fill=tk.X, padx=10, pady=(0, 10))
        ttk.Label(av_frame, text="倍速:").pack(side=tk.LEFT)
        controller.tempo_var = tk.DoubleVar(value=1.0)
        ttk.Spinbox(av_frame, from_=0.25, to=3.0, increment=0.05, textvariable=controller.tempo_var, width=8).pack(side=tk.LEFT, padx=(10, 16))

        # 定时触发（单次）
        timing_frame = ttk.LabelFrame(ctrl_inner, text="定时触发", padding="10")
        timing_frame.pack(side=tk.TOP, fill=tk.X, padx=10, pady=(0, 10))
        # 时间输入：HH:MM:SS.mmm
        ttk.Label(timing_frame, text="目标时间(24h):").grid(row=0, column=0, sticky=tk.W)
        controller.timing_hh_var = tk.IntVar(value=17)
        controller.timing_mm_var = tk.IntVar(value=0)
        controller.timing_ss_var = tk.IntVar(value=0)
        controller.timing_ms_var = tk.IntVar(value=0)
        ttk.Spinbox(timing_frame, from_=0, to=23, width=4, textvariable=controller.timing_hh_var).grid(row=0, column=1, sticky=tk.W)
        ttk.Label(timing_frame, text=":").grid(row=0, column=2, sticky=tk.W)
        ttk.Spinbox(timing_frame, from_=0, to=59, width=4, textvariable=controller.timing_mm_var).grid(row=0, column=3, sticky=tk.W)
        ttk.Label(timing_frame, text=":").grid(row=0, column=4, sticky=tk.W)
        ttk.Spinbox(timing_frame, from_=0, to=59, width=4, textvariable=controller.timing_ss_var).grid(row=0, column=5, sticky=tk.W)
        ttk.Label(timing_frame, text=".").grid(row=0, column=6, sticky=tk.W)
        ttk.Spinbox(timing_frame, from_=0, to=999, width=6, textvariable=controller.timing_ms_var).grid(row=0, column=7, sticky=tk.W)

        # 对时控制（仅保留启用公网对时）
        def _btn_enable_net():
            getattr(controller.playback_controller, '_timing_enable_network_clock', lambda: None)()
            _refresh_timing_status(delay_ms=50)
        ttk.Button(timing_frame, text="启用公网对时", command=_btn_enable_net).grid(row=1, column=0, sticky=tk.W, pady=(6,0))

        # NTP 服务器设置（简化）
        ntp_frame = ttk.Frame(timing_frame)
        ntp_frame.grid(row=4, column=0, columnspan=8, sticky=tk.W+tk.E, pady=(6,0))
        ttk.Label(ntp_frame, text="NTP服务器:").pack(side=tk.LEFT)
        try:
            default_servers = "ntp.ntsc.ac.cn,time1.cloud.tencent.com"
            controller.timing_servers_var = tk.StringVar(value=default_servers)
        except Exception:
            pass
        server_entry = ttk.Entry(ntp_frame, textvariable=controller.timing_servers_var, width=30)
        server_entry.pack(side=tk.LEFT, padx=(6,12))
        controller.ntp_enabled_var = tk.BooleanVar(value=True)
        def _toggle_ntp():
            getattr(controller.playback_controller, '_timing_toggle_ntp', lambda *_: None)(controller.ntp_enabled_var.get())
            _refresh_timing_status(delay_ms=100)
        ttk.Checkbutton(ntp_frame, text="启用NTP", variable=controller.ntp_enabled_var, command=_toggle_ntp).pack(side=tk.LEFT)

        # 对时参数：间隔与重排阈值
        ttk.Label(timing_frame, text="对时间隔(s):").grid(row=5, column=0, sticky=tk.W, pady=(6,0))
        controller.timing_resync_interval_var = tk.DoubleVar(value=1.0)
        ttk.Spinbox(timing_frame, from_=0.2, to=10.0, increment=0.2, width=8, textvariable=controller.timing_resync_interval_var).grid(row=5, column=1, sticky=tk.W, padx=(6,0), pady=(6,0))
        ttk.Label(timing_frame, text="重排阈值(ms):").grid(row=5, column=2, sticky=tk.W, pady=(6,0))
        controller.timing_adjust_threshold_var = tk.DoubleVar(value=5.0)
        ttk.Spinbox(timing_frame, from_=1.0, to=1000.0, increment=1.0, width=10, textvariable=controller.timing_adjust_threshold_var).grid(row=5, column=3, sticky=tk.W, padx=(6,0), pady=(6,0))
        def _apply_resync_params():
            try:
                interval = float(controller.timing_resync_interval_var.get())
            except Exception:
                interval = None
            try:
                thr = float(controller.timing_adjust_threshold_var.get())
            except Exception:
                thr = None
            getattr(controller.playback_controller, '_timing_set_resync_settings', lambda *_: None)(interval, thr)
            _refresh_timing_status(delay_ms=100)
        ttk.Button(timing_frame, text="应用对时参数", style=styles['secondary'], command=_apply_resync_params).grid(row=5, column=4, sticky=tk.W, padx=(6,0), pady=(6,0))

        # 手动补偿与状态
        ttk.Label(timing_frame, text="手动补偿(ms):").grid(row=2, column=0, sticky=tk.W, pady=(6,0))
        controller.timing_manual_ms_var = tk.IntVar(value=0)
        ttk.Spinbox(timing_frame, from_=-2000, to=2000, increment=1, width=8, textvariable=controller.timing_manual_ms_var).grid(row=2, column=1, sticky=tk.W, padx=(6,0), pady=(6,0))
        controller.timing_status_var = tk.StringVar(value="状态: 未对时")
        ttk.Label(timing_frame, textvariable=controller.timing_status_var, foreground="#666").grid(row=2, column=2, columnspan=6, sticky=tk.W, padx=(12,0), pady=(6,0))

        # 操作按钮（合并创建/取消计划按钮）
        def _toggle_schedule():
            pc = getattr(controller, 'playback_controller', None)
            if not pc:
                return
            
            # 检查当前按钮状态
            current_text = controller.schedule_button.cget("text")
            if current_text == "创建计划":
                # 创建计划
                pc._timing_schedule_for_current_instrument()
                # 延迟更新按钮状态
                controller.root.after(100, lambda: _update_schedule_button_state())
            elif current_text == "取消计划":
                # 取消计划
                pc._timing_cancel_schedule()
                # 延迟更新按钮状态
                controller.root.after(100, lambda: _update_schedule_button_state())
            
            _refresh_timing_status(delay_ms=100)
        
        def _update_schedule_button_state():
            """更新计划按钮状态（根据实际计划状态）"""
            try:
                pc = getattr(controller, 'playback_controller', None)
                if not pc or not hasattr(controller, 'schedule_button'):
                    return
                
                # 检查播放控制器是否有活跃的计划
                has_schedule = False
                try:
                    if (hasattr(pc, '_last_schedule_id') and pc._last_schedule_id):
                        has_schedule = True
                except Exception:
                    pass
                
                if has_schedule:
                    controller.schedule_button.configure(text="取消计划", style=styles['danger'])
                else:
                    controller.schedule_button.configure(text="创建计划", style=styles['primary'])
            except Exception as e:
                try:
                    controller._log_message(f"更新计划按钮状态失败: {e}", "ERROR")
                except Exception:
                    pass
        
        def _btn_test_now():
            getattr(controller.playback_controller, '_timing_test_now', lambda: None)()
            _refresh_timing_status(delay_ms=50)
        
        # 创建合并的调度按钮
        controller.schedule_button = ttk.Button(timing_frame, text="创建计划", style=styles['primary'], command=_toggle_schedule)
        controller.schedule_button.grid(row=3, column=0, sticky=tk.W, pady=(8,0))
        ttk.Button(timing_frame, text="立即测试", style=styles['info'], command=_btn_test_now).grid(row=3, column=1, sticky=tk.W, padx=(6,0), pady=(8,0))
        for c in range(8):
            try:
                timing_frame.grid_columnconfigure(c, weight=1)
            except Exception:
                pass

        # 状态刷新函数：读取控制器桥接，显示 provider/sys_delta/rtt/manual/auto/net_shift/local_chain/next/倒计时（分行，保留两位小数）
        def _refresh_timing_status(delay_ms: int = 0):
            def _do():
                try:
                    pc = getattr(controller, 'playback_controller', None)
                    if pc and hasattr(pc, '_timing_get_ui_status'):
                        st = pc._timing_get_ui_status() or {}
                        provider = st.get('provider', 'Local')
                        delta = st.get('sys_delta_ms', 0.0)
                        rtt = st.get('rtt_ms', 0.0)
                        manual = st.get('manual_compensation_ms', 0.0)
                        auto_latency = st.get('auto_latency_ms', None)
                        net_shift = st.get('net_shift_ms', None)
                        local_chain = st.get('local_chain_ms', None)
                        next_fire = st.get('next_fire', '')
                        remaining = st.get('remaining_ms')
                        # 倒计时格式化
                        def _fmt_ms(ms):
                            try:
                                ms = int(ms)
                                s, msec = divmod(ms, 1000)
                                h, rem = divmod(s, 3600)
                                m, sec = divmod(rem, 60)
                                return f"{h:02d}:{m:02d}:{sec:02d}.{msec:03d}"
                            except Exception:
                                return "--:--:--.---"
                        lines = []
                        lines.append(f"来源: {provider}")
                        lines.append(f"NTP-本地偏差: {float(delta):.2f} ms")
                        lines.append(f"网络往返延迟: {float(rtt):.2f} ms")
                        if auto_latency is not None:
                            try:
                                lines.append(f"自动延迟(估计): {float(auto_latency):.2f} ms")
                            except Exception:
                                lines.append(f"自动延迟(估计): {auto_latency} ms")
                        lines.append(f"手动补偿: {float(manual):.2f} ms")
                        if net_shift is not None:
                            try:
                                lines.append(f"合成偏移(正=延后,负=提前): {float(net_shift):.2f} ms")
                            except Exception:
                                lines.append(f"合成偏移(正=延后,负=提前): {net_shift} ms")
                        if local_chain is not None:
                            try:
                                lines.append(f"本地链路延迟: {float(local_chain):.2f} ms")
                            except Exception:
                                lines.append(f"本地链路延迟: {local_chain} ms")
                        if next_fire:
                            lines.append(f"下一次: {next_fire}")
                        if remaining is not None:
                            lines.append(f"倒计时: {_fmt_ms(remaining)}")
                        controller.timing_status_var.set("\n".join(lines))
                except Exception:
                    pass
                # 循环刷新
                try:
                    if hasattr(controller, 'root') and controller.root:
                        controller.root.after(1000, _refresh_timing_status)
                except Exception:
                    pass
            try:
                if hasattr(controller, 'root') and controller.root:
                    controller.root.after(delay_ms or 0, _do)
                else:
                    _do()
            except Exception:
                _do()

        # 首次进入时刷新一次，并开启定时刷新
        _refresh_timing_status(delay_ms=10)
        # 页面初始化时禁用自动对时，避免程序启动时触发对时
        # def _bootstrap_timing():
        #     try:
        #         if controller.ntp_enabled_var.get():
        #             _apply_servers()
        #             _apply_resync_params()
        #     except Exception:
        #         pass
        # try:
        #     if hasattr(controller, 'root') and controller.root:
        #         controller.root.after(50, _bootstrap_timing)
        #     else:
        #         _bootstrap_timing()
        # except Exception:
        #     _bootstrap_timing()
        # 循环将在 _do 中自行重排 next after

        # 操作区域已整合到主要的演奏控制区域中，此处跳过
    except Exception as e:
        # 加载失败兜底
        try:
            controller._log_message(f"控制区创建失败: {e}", "ERROR")
        except Exception:
            pass
        ttk.Label(ctrl_inner, text="播放控制加载失败", foreground="#B00020").pack(anchor=tk.W, padx=12, pady=12)

    # 分部识别与选择（内嵌，不使用弹窗）
    part_frame = ttk.LabelFrame(ctrl_inner, text="分部识别与选择", padding="8")
    part_frame.pack(side=tk.TOP, fill=tk.BOTH, expand=True, padx=10, pady=(0, 10))

    # 工具栏：识别 + 批量选择 + 拆分模式
    part_toolbar = ttk.Frame(part_frame)
    part_toolbar.pack(side=tk.TOP, fill=tk.X)
    # 内置识别分部：按 轨/通道/音色 分离，并自动填充表格
    def _identify_parts():
        try:
            # 使用 PlaybackController 的 split 能力
            pc = getattr(controller, 'playback_controller', None)
            midi_path = getattr(controller, 'midi_path_var', None).get() if hasattr(controller, 'midi_path_var') else ''
            if not pc or not midi_path:
                return
            parts = pc.split_midi_by_track_channel(midi_path)
            controller._last_split_parts = parts or {}
            # 自动根据乐器偏好勾选（非鼓勾选非 ch9；鼓勾选 ch9）
            controller._selected_part_names = set()
            # 将当前选择推送到服务层过滤器
            try:
                ps = getattr(controller, 'playback_service', None)
                if ps and hasattr(ps, 'set_selected_parts_filter'):
                    sel = list(getattr(controller, '_selected_part_names', set()) or [])
                    ps.set_selected_parts_filter(getattr(controller, '_last_split_parts', {}) or {}, sel)
            except Exception:
                pass
            try:
                controller._log_message(f"分部识别完成: {len(parts)} 个", "INFO")
            except Exception:
                pass
        except Exception:
            pass
    ttk.Button(part_toolbar, text="识别分部", style=styles['info'], command=_identify_parts).pack(side=tk.LEFT)
    ttk.Button(part_toolbar, text="全选", style=styles['primary'], command=getattr(controller, '_ui_parts_select_all', lambda: None)).pack(side=tk.LEFT, padx=(8,0))
    ttk.Button(part_toolbar, text="全不选", style=styles['warning'], command=getattr(controller, '_ui_parts_select_none', lambda: None)).pack(side=tk.LEFT, padx=(8,0))
    ttk.Button(part_toolbar, text="反选", style=styles['secondary'], command=getattr(controller, '_ui_parts_select_invert', lambda: None)).pack(side=tk.LEFT, padx=(8,0))
    # 拆分模式选择
    ttk.Label(part_toolbar, text="拆分模式:").pack(side=tk.LEFT, padx=(16,4))
    controller.partition_split_mode_var = tk.StringVar(value='仅通道')
    ttk.Combobox(part_toolbar, textvariable=controller.partition_split_mode_var, state='readonly', width=14,
                 values=['仅通道','智能聚类']).pack(side=tk.LEFT)

    # Treeview：展示分部
    part_body = ttk.Frame(part_frame)
    part_body.pack(side=tk.TOP, fill=tk.BOTH, expand=True, pady=(6, 6))
    # 勾选列 + 数据列
    columns = ("选择", "分部", "计数", "说明")
    parts_tree = ttk.Treeview(part_body, columns=columns, show='headings', selectmode='none', height=8)
    heads = ["选择", "分部", "计数", "说明"]
    widths = [60, 240, 80, 300]
    for i, col in enumerate(columns):
        parts_tree.heading(col, text=heads[i])
        if i == 0:
            parts_tree.column(col, width=widths[i], anchor=tk.CENTER)
        elif i == 2:
            parts_tree.column(col, width=widths[i], anchor=tk.CENTER)
        else:
            parts_tree.column(col, width=widths[i], anchor=tk.W)
    vbar2 = ttk.Scrollbar(part_body, orient=tk.VERTICAL, command=parts_tree.yview)
    parts_tree.configure(yscrollcommand=vbar2.set)
    parts_tree.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
    vbar2.pack(side=tk.RIGHT, fill=tk.Y)
    # 对外暴露句柄
    controller._parts_tree = parts_tree
    # 勾选状态存储：name -> bool
    controller._parts_checked = {}

    def _on_parts_tree_click(event=None):
        try:
            region = parts_tree.identify('region', event.x, event.y)
            if region != 'cell':
                return
            col = parts_tree.identify_column(event.x)
            if col != '#1':  # 仅在“选择”列点按时切换
                return
            row = parts_tree.identify_row(event.y)
            if not row:
                return
            vals = list(parts_tree.item(row, 'values'))
            if not vals or len(vals) < 2:
                return
            name = vals[1]
            sel = getattr(controller, '_selected_part_names', set()) or set()
            if name in sel:
                sel.remove(name)
                vals[0] = '□'
            else:
                sel.add(name)
                vals[0] = '■'
            controller._selected_part_names = sel
            parts_tree.item(row, values=vals)
            # 将选择推送到服务层过滤器（空集合将清除过滤）
            try:
                ps = getattr(controller, 'playback_service', None)
                if ps and hasattr(ps, 'set_selected_parts_filter'):
                    ps.set_selected_parts_filter(getattr(controller, '_last_split_parts', {}) or {}, list(sel))
            except Exception:
                pass
            # 勾选变化后，立即重新计算白键率缓存（不播放）
            _schedule_compute_white_rate(100)
        except Exception:
            pass
    parts_tree.bind('<Button-1>', _on_parts_tree_click)

    # 操作行：应用所选并解析 / 播放 / 导出
    act_row = ttk.Frame(part_frame)
    act_row.pack(side=tk.TOP, anchor=tk.W)
    ttk.Button(act_row, text="应用所选分部并解析", style=styles['primary'], command=getattr(controller, '_ui_apply_selected_parts_and_analyze', lambda: None)).pack(side=tk.LEFT)
    ttk.Button(act_row, text="播放所选分部", style=styles['success'], command=getattr(controller, '_ui_play_selected_partitions', lambda: None)).pack(side=tk.LEFT, padx=(8,0))
    ttk.Button(act_row, text="导出所选分部", style=styles['secondary'], command=getattr(controller, '_ui_export_selected_partitions', lambda: None)).pack(side=tk.LEFT, padx=(8,0))

    # 合奏相关（可选）
    if include_ensemble:
        ensemble_frame = ttk.Frame(ctrl_inner)
        ensemble_frame.pack(side=tk.TOP, anchor=tk.W, pady=(6, 0))
        controller._ensemble_label = ttk.Label(ensemble_frame, text="合奏模式:")
        controller._ensemble_label.pack(side=tk.LEFT)
        controller.ensemble_mode_var = tk.StringVar(value='ensemble')
        ttk.Radiobutton(ensemble_frame, text="大合奏", variable=controller.ensemble_mode_var, value='ensemble').pack(side=tk.LEFT, padx=(6, 0))
        ttk.Radiobutton(ensemble_frame, text="独奏", variable=controller.ensemble_mode_var, value='solo').pack(side=tk.LEFT, padx=(6, 12))
        controller._role_label = ttk.Label(ensemble_frame, text="我的角色:")
        controller._role_label.pack(side=tk.LEFT)
        controller.my_role_var = tk.StringVar(value='melody')
        controller.my_role_display_var = tk.StringVar(value='旋律')
        controller._role_cn2en = {"旋律": "melody", "吉他": "guitar", "贝斯": "bass", "鼓": "drums"}
        controller._role_en2cn = {v: k for k, v in controller._role_cn2en.items()}
        controller._my_role_combo = ttk.Combobox(ensemble_frame, textvariable=controller.my_role_display_var, state="readonly", width=8,
                                               values=list(controller._role_cn2en.keys()))
        def _on_my_role_select(event=None):
            try:
                cn = controller.my_role_display_var.get()
                en = controller._role_cn2en.get(cn, 'melody')
                controller.my_role_var.set(en)
            except Exception:
                pass
        controller._my_role_combo.bind('<<ComboboxSelected>>', _on_my_role_select)
        controller._my_role_combo.pack(side=tk.LEFT)
        try:
            controller.ensemble_mode_var.trace_add("write", lambda *args: controller._on_ensemble_mode_changed())
        except Exception:
            pass
        controller._ensemble_controls_row = ensemble_frame

        # 保存合奏页签引用，便于外部显隐
        controller._tab_ensemble = tab_ensemble

        # 旧的“计划/对时/统一开始”UI已移除，统一在“控制”分页的“定时触发”中提供。

    # 不返回未定义的 notebook，避免 NameError
    return None
