#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
架子鼓页面：独立完整的架子鼓演奏界面
包含分部解析、播放控制、播放列表、事件表、日志等完整功能
"""
from __future__ import annotations
import tkinter as tk
from tkinter import ttk, filedialog, messagebox
import os
from typing import Optional, Dict, List, Any

try:
    from .. import BasePage
except Exception:
    class BasePage:
        def mount(self, left, right): ...
        def unmount(self): ...


class DrumsPage(BasePage):
    def __init__(self, controller, app_ref=None):
        self.controller = controller
        self.app_ref = app_ref
        self._mounted = False
        
        # 设置当前乐器，以便播放控制器能够识别
        if self.app_ref:
            self.app_ref.current_instrument = "架子鼓"
            # 为播放服务添加必要的属性
            self.app_ref.analysis_notes = None
            self.app_ref.analysis_file = ""
        
        # 状态变量
        # midi_path_var将在_create_file_section中定义在app_ref上
        self.current_midi_file = ""
        self.analysis_notes = None
        self.analysis_file = ""
        
        # UI组件引用
        self.partition_listbox: Optional[tk.Listbox] = None
        self.event_tree: Optional[ttk.Treeview] = None
        self.playlist_tree: Optional[ttk.Treeview] = None
        self.log_text: Optional[tk.Text] = None
        
        # 播放控制按钮
        self.btn_play: Optional[ttk.Button] = None
        self.btn_pause: Optional[ttk.Button] = None
        self.btn_stop: Optional[ttk.Button] = None
        
        # 分部解析相关
        self.partitions_data = []
        self.selected_partitions = []
        # 播放列表与模式
        self._playlist_paths: Dict[str, str] = {}
        self.playlist_mode_var = tk.StringVar(value='顺序')  # 顺序/循环/单曲
        self._current_playing_iid: Optional[str] = None

        # 定时和对时相关
        self._current_schedule_id: Optional[str] = None

    def mount(self, left: ttk.Frame, right: ttk.Frame):
        """挂载架子鼓页面"""
        # 与其他乐器页面保持一致，使用统一的播放控制组件
        try:
            # 关键：避免在同一容器既 pack 又 grid —— 创建子容器供组件使用（组件内部可使用 grid）
            content = ttk.Frame(left)
            content.pack(fill=tk.BOTH, expand=True)
            
            # 使用统一的播放控制组件，与其他乐器页面保持一致
            include_ensemble = False  # 架子鼓不需要合奏模式
            if self.app_ref:
                self.app_ref._create_playback_control_component(content, include_ensemble=include_ensemble, instrument='架子鼓')
            else:
                self._log_message("app_ref 不可用，无法使用统一播放控制组件", "ERROR")
            
            # 在统一组件基础上，添加架子鼓专属的分部解析功能
            self._create_drums_partition_section(content)
            
        except Exception as e:
            self._log_message(f"架子鼓页面挂载失败: {e}", "ERROR")
            import traceback
            traceback.print_exc()
        
        # 右侧已移除，与其他乐器页面保持一致
        self._mounted = True

        # 向 DrumsController 注入 app_ref，便于其通过 event_bus 发布播放事件
        try:
            if hasattr(self.controller, 'set_app_ref'):
                self.controller.set_app_ref(self.app_ref)
                print("[DEBUG] 架子鼓页面：已向 DrumsController 注入 app_ref")
        except Exception as e:
            print(f"[DEBUG] 架子鼓页面：注入 app_ref 到 DrumsController 失败: {e}")

        # 订阅播放事件，联动操作栏按钮
        try:
            self._bind_playback_events()
        except Exception as e:
            print(f"[DEBUG] 架子鼓页面：绑定播放事件失败: {e}")

    def unmount(self):
        """卸载页面"""
        self._mounted = False
        try:
            self._unbind_playback_events()
        except Exception:
            pass


    # 文件选择功能已由统一播放控制组件提供，不再需要独立实现

    def _create_drums_partition_section(self, parent):
        """创建架子鼓专属的分部解析区域"""
        # 在统一组件的基础上，添加架子鼓专属的分部解析功能
        # 这个区域会在统一组件的控制分页中添加
        # 架子鼓的分部解析功能可以通过统一组件的解析设置分页来访问
        pass

    # 播放控制功能已由统一播放控制组件提供，不再需要独立实现

    # 定时功能已由统一播放控制组件提供，不再需要独立实现
    
    def _create_timing_section(self, parent):
        """创建定时触发控制组件（鼓专用：定时回调走 DrumsController）"""
        timing_frame = ttk.LabelFrame(parent, text="定时触发（NTP对时·架子鼓）", padding="10")
        timing_frame.pack(fill=tk.X, pady=(6, 0))
        
        try:
            # 简化的定时控制组件创建
            from pages.components import timing_controls
            
            # 定时播放回调：复用主控制栏“开始演奏”入口，按钮联动由统一组件处理
            def drums_play_callback():
                """架子鼓定时播放回调：返回bool表示是否成功"""
                try:
                    # 设置当前乐器
                    if hasattr(self.app_ref, 'current_instrument'):
                        self.app_ref.current_instrument = '架子鼓'

                    # 选取要播放的文件路径：播放列表当前项 -> 页面当前文件 -> app当前文件
                    path = None
                    try:
                        pm = getattr(self.app_ref, 'playlist_manager', None)
                        if pm and hasattr(pm, 'get_current_path'):
                            path = pm.get_current_path()
                    except Exception:
                        path = None
                    if not path:
                        path = getattr(self, 'current_midi_file', '') or getattr(self.app_ref, 'current_midi_path', '')
                    if not path:
                        self._log_message("定时触发失败：未找到可播放的MIDI文件", "ERROR")
                        return False

                    # 同步到 app_ref，复用统一开始入口
                    try:
                        self.app_ref.current_midi_path = path
                    except Exception:
                        pass

                    # 关键：设置统一入口使用的 midi_path_var，避免去查找 app 自己的播放列表
                    try:
                        if hasattr(self.app_ref, 'midi_path_var') and self.app_ref.midi_path_var is not None:
                            self.app_ref.midi_path_var.set(path)
                    except Exception:
                        pass

                    # 同步倍速到 app_ref（如有）
                    try:
                        if hasattr(self.app_ref, 'tempo_var') and self.app_ref.tempo_var is not None and self.tempo_var is not None:
                            self.app_ref.tempo_var.set(self.tempo_var.get())
                    except Exception:
                        pass

                    # 复用主控制栏“开始演奏”入口（会自动联动按钮为 暂停/停止）
                    if hasattr(self.app_ref, '_start_auto_play'):
                        try:
                            # 确保在主线程执行，以便安全更新Tk控件
                            if hasattr(self.app_ref, 'root') and getattr(self.app_ref, 'root') is not None:
                                self.app_ref.root.after(0, self.app_ref._start_auto_play)
                            else:
                                # 兜底直接调用（某些测试环境无root）
                                self.app_ref._start_auto_play()
                        except Exception:
                            # 兜底直接调用
                            try:
                                self.app_ref._start_auto_play()
                            except Exception:
                                self._log_message("调用 _start_auto_play 失败", "ERROR")
                                return False
                        return True
                    else:
                        self._log_message("应用入口不可用：_start_auto_play 缺失", "ERROR")
                        return False
                except Exception as e:
                    print(f"[DEBUG] 架子鼓定时播放失败: {e}")
                    return False

            timing_controls.create_timing_controls(
                parent=timing_frame,
                app_ref=self.app_ref,
                controller_ref=self.controller,
                instrument_name="架子鼓",
                play_callback=drums_play_callback
            )
        except Exception as e:
            ttk.Label(timing_frame, text=f"定时控制组件加载失败: {e}", foreground="red").pack()

    # ===== 播放事件绑定：驱动按钮联动 =====
    def _bind_playback_events(self):
        self._evt_tokens = getattr(self, '_evt_tokens', [])
        bus = getattr(self.app_ref, 'event_bus', None)
        if not bus:
            return
        def on_start(_):
            try:
                self._update_button_states(playing=True)
            except Exception:
                pass
        def on_stop(_):
            try:
                self._update_button_states(playing=False)
            except Exception:
                pass
        def on_pause(_):
            # 暂停时按钮一般切换为“恢复/停止”，此处保持 playing=True（由上层按钮自身文案处理）
            try:
                self._update_button_states(playing=True)
            except Exception:
                pass
        def on_resume(_):
            try:
                self._update_button_states(playing=True)
            except Exception:
                pass
        def on_complete(_):
            try:
                self._update_button_states(playing=False)
            except Exception:
                pass
        try:
            self._evt_tokens.append(bus.subscribe(getattr(self.app_ref.Events if hasattr(self.app_ref, 'Events') else __import__('app').event_bus, 'Events').PLAYBACK_START, on_start))
        except Exception:
            self._evt_tokens.append(bus.subscribe('playback.start', on_start))
        try:
            self._evt_tokens.append(bus.subscribe('playback.stop', on_stop))
            self._evt_tokens.append(bus.subscribe('playback.pause', on_pause))
            self._evt_tokens.append(bus.subscribe('playback.resume', on_resume))
            self._evt_tokens.append(bus.subscribe('playback.complete', on_complete))
        except Exception:
            pass

    def _unbind_playback_events(self):
        bus = getattr(self.app_ref, 'event_bus', None)
        tokens = getattr(self, '_evt_tokens', [])
        if not bus or not tokens:
            return
        try:
            for t in tokens:
                try:
                    bus.unsubscribe(t)
                except Exception:
                    pass
        finally:
            self._evt_tokens = []

    def _create_playlist_section(self, parent):
        """创建播放列表区域"""
        playlist_frame = ttk.LabelFrame(parent, text="播放列表", padding="10")
        playlist_frame.pack(fill=tk.BOTH, expand=True)
        
        # 播放列表树形控件
        tree_frame = ttk.Frame(playlist_frame)
        tree_frame.pack(fill=tk.BOTH, expand=True)
        
        self.playlist_tree = ttk.Treeview(tree_frame, columns=("type", "status"), 
                                         show="tree headings", height=4)
        self.playlist_tree.heading("#0", text="文件名", anchor=tk.W)
        self.playlist_tree.heading("type", text="类型", anchor=tk.W)
        self.playlist_tree.heading("status", text="演奏状态", anchor=tk.W)
        
        self.playlist_tree.column("#0", width=200)
        self.playlist_tree.column("type", width=80)
        self.playlist_tree.column("status", width=80)
        
        playlist_scroll = ttk.Scrollbar(tree_frame, orient=tk.VERTICAL,
                                       command=self.playlist_tree.yview)
        self.playlist_tree.configure(yscrollcommand=playlist_scroll.set)
        
        self.playlist_tree.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        playlist_scroll.pack(side=tk.RIGHT, fill=tk.Y)

        # 工具栏：添加/移除/清空 + 播放控制 + 模式
        toolbar = ttk.Frame(playlist_frame)
        toolbar.pack(fill=tk.X, pady=(6,0))
        ttk.Button(toolbar, text="添加文件", command=self._browse_file, style='MF.Success.TButton').pack(side=tk.LEFT)
        ttk.Button(toolbar, text="移除所选", command=self._remove_selected_from_playlist, style='MF.Warning.TButton').pack(side=tk.LEFT, padx=(6,0))
        ttk.Button(toolbar, text="清空", command=self._clear_playlist, style='MF.Danger.TButton').pack(side=tk.LEFT, padx=(6,0))
        ttk.Button(toolbar, text="播放所选", command=self._play_selected_from_playlist, style='MF.Info.TButton').pack(side=tk.LEFT, padx=(12,0))
        ttk.Button(toolbar, text="上一首", command=self._play_prev_from_playlist, style='MF.Primary.TButton').pack(side=tk.LEFT, padx=(6,0))
        ttk.Button(toolbar, text="下一首", command=self._play_next_from_playlist, style='MF.Primary.TButton').pack(side=tk.LEFT, padx=(6,0))
        ttk.Label(toolbar, text="播放模式:").pack(side=tk.LEFT, padx=(16,4))
        mode_combo = ttk.Combobox(toolbar, textvariable=self.playlist_mode_var, state='readonly', width=10,
                                  values=['单曲','顺序','循环'])
        mode_combo.pack(side=tk.LEFT)

    def _create_right_panel(self, parent):
        """创建右侧面板"""
        # 创建笔记本控件用于标签页
        notebook = ttk.Notebook(parent)
        notebook.pack(fill=tk.BOTH, expand=True, padx=5, pady=5)
        
        # 事件表标签页
        event_frame = ttk.Frame(notebook)
        notebook.add(event_frame, text="事件表")
        self._create_event_table(event_frame)
        
        # 日志标签页
        log_frame = ttk.Frame(notebook)
        notebook.add(log_frame, text="日志")
        self._create_log_panel(log_frame)

    def _create_event_table(self, parent):
        """创建事件表"""
        tree_frame = ttk.Frame(parent)
        tree_frame.pack(fill=tk.BOTH, expand=True, padx=5, pady=5)
        
        self.event_tree = ttk.Treeview(tree_frame, columns=("time", "type", "note", "velocity"),
                                      show="headings")
        
        self.event_tree.heading("time", text="时间", anchor=tk.W)
        self.event_tree.heading("type", text="类型", anchor=tk.W)
        self.event_tree.heading("note", text="音符", anchor=tk.W)
        self.event_tree.heading("velocity", text="力度", anchor=tk.W)
        
        self.event_tree.column("time", width=80)
        self.event_tree.column("type", width=60)
        self.event_tree.column("note", width=80)
        self.event_tree.column("velocity", width=60)
        
        event_scroll = ttk.Scrollbar(tree_frame, orient=tk.VERTICAL,
                                    command=self.event_tree.yview)
        self.event_tree.configure(yscrollcommand=event_scroll.set)
        
        self.event_tree.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        event_scroll.pack(side=tk.RIGHT, fill=tk.Y)

    def _create_log_panel(self, parent):
        """创建日志面板"""
        log_frame = ttk.Frame(parent)
        log_frame.pack(fill=tk.BOTH, expand=True, padx=5, pady=5)
        
        self.log_text = tk.Text(log_frame, wrap=tk.WORD, height=15)
        log_scroll = ttk.Scrollbar(log_frame, orient=tk.VERTICAL,
                                  command=self.log_text.yview)
        self.log_text.configure(yscrollcommand=log_scroll.set)
        
        self.log_text.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        log_scroll.pack(side=tk.RIGHT, fill=tk.Y)
        
        # 初始日志
        self._log_message("架子鼓模式已加载")
        self._log_message("支持单轨道和多轨道MIDI文件的第10轨道（通道9）")

    # ===== 事件处理方法 =====
    
    def _browse_file(self):
        """浏览选择MIDI文件"""
        file_path = filedialog.askopenfilename(
            title="选择架子鼓MIDI文件",
            filetypes=[('MIDI Files', '*.mid *.midi'), ('All Files', '*.*')]
        )
        if file_path:
            midi_path_var = self.app_ref.midi_path_var if self.app_ref else self.midi_path_var
            midi_path_var.set(file_path)
            self.current_midi_file = file_path
            self._log_message(f"已选择文件: {os.path.basename(file_path)}")
            self._add_to_playlist(file_path)

    def _identify_partitions(self):
        """识别MIDI分部"""
        if not self.current_midi_file:
            messagebox.showwarning("提示", "请先选择MIDI文件")
            return
            
        try:
            self._log_message("正在识别分部...")
            
            # 调用架子鼓解析器识别分部
            if hasattr(self.app_ref, 'drums_parser'):
                parser = self.app_ref.drums_parser
                partitions = parser.identify_partitions(self.current_midi_file)
                
                self.partitions_data = partitions
                self._update_partition_list()
                self._log_message(f"识别到 {len(partitions)} 个分部")
            else:
                # 简化版本：假设架子鼓在第10轨道
                self.partitions_data = [
                    {"track": 9, "channel": 9, "name": "架子鼓", "notes": 0}
                ]
                self._update_partition_list()
                self._log_message("使用默认架子鼓分部（轨道10，通道9）")
                
        except Exception as e:
            self._log_message(f"分部识别失败: {e}", "ERROR")

    def _update_partition_list(self):
        """更新分部列表显示"""
        if not self.partition_listbox:
            return
            
        self.partition_listbox.delete(0, tk.END)
        for i, partition in enumerate(self.partitions_data):
            name = partition.get("name", f"分部{i+1}")
            track = partition.get("track", 0)
            channel = partition.get("channel", 0)
            notes = partition.get("notes", 0)
            
            display_text = f"{name} (轨道{track+1}, 通道{channel+1}, {notes}音符)"
            self.partition_listbox.insert(tk.END, display_text)

    def _select_all_partitions(self):
        """全选所有分部"""
        if not self.partition_listbox:
            return
            
        self.partition_listbox.select_set(0, tk.END)
        self._log_message("已全选所有分部")

    def _apply_analysis(self):
        """应用分部解析"""
        if not self.partitions_data:
            messagebox.showwarning("提示", "请先识别分部")
            return
            
        selected_indices = self.partition_listbox.curselection()
        if not selected_indices:
            messagebox.showwarning("提示", "请选择要解析的分部")
            return
            
        try:
            self._log_message("正在解析选中的分部...")
            
            # 获取选中的分部
            self.selected_partitions = [self.partitions_data[i] for i in selected_indices]
            
            # 调用架子鼓解析器
            if hasattr(self.app_ref, 'drums_parser'):
                parser = self.app_ref.drums_parser
                analysis_notes = parser.parse_partitions(
                    self.current_midi_file, 
                    self.selected_partitions
                )
                # 同时保存到self和app_ref上
                self.analysis_notes = analysis_notes
                self.analysis_file = self.current_midi_file
                if self.app_ref:
                    self.app_ref.analysis_notes = analysis_notes
                    self.app_ref.analysis_file = self.current_midi_file
                
                self._update_event_table()
                self._log_message(f"解析完成，共 {len(self.analysis_notes)} 个事件")
            else:
                self._log_message("架子鼓解析器不可用", "WARNING")
                
        except Exception as e:
            self._log_message(f"解析失败: {e}", "ERROR")

    def _update_event_table(self):
        """更新事件表显示"""
        if not self.event_tree or not self.analysis_notes:
            return
            
        # 清空现有项目
        for item in self.event_tree.get_children():
            self.event_tree.delete(item)
            
        # 添加解析的事件
        for i, note in enumerate(self.analysis_notes[:100]):  # 限制显示前100个事件
            time_str = f"{note.get('time', 0):.2f}s"
            note_type = "打击" if note.get('type') == 'note_on' else "释放"
            note_name = self._get_drum_name(note.get('note', 0))
            velocity = note.get('velocity', 0)
            
            self.event_tree.insert("", tk.END, values=(time_str, note_type, note_name, velocity))

    def _get_drum_name(self, note_number):
        """根据MIDI音符号获取鼓件名称"""
        drum_names = {
            35: "底鼓1", 36: "底鼓2", 37: "侧击", 38: "军鼓1", 39: "拍手",
            40: "军鼓2", 41: "低嗵鼓", 42: "踩镲闭", 43: "低嗵鼓", 44: "踩镲踏板",
            45: "中嗵鼓", 46: "踩镲开", 47: "中高嗵鼓", 48: "高嗵鼓", 49: "吊镲1",
            50: "高嗵鼓", 51: "叮叮镲", 52: "中国镲", 53: "叮叮镲", 54: "铃鼓",
            55: "溅镲", 56: "牛铃", 57: "吊镲2", 58: "颤音鼓", 59: "叮叮镲",
            60: "高邦戈鼓", 61: "低邦戈鼓", 62: "哑音康加鼓", 63: "开音康加鼓",
            64: "低康加鼓", 65: "高音鼓", 66: "低音鼓", 67: "高阿戈戈铃",
            68: "低阿戈戈铃", 69: "响葫芦", 70: "短口哨", 71: "长口哨",
            72: "短刮葫芦", 73: "长刮葫芦", 74: "响棒", 75: "木鱼",
            76: "木块", 77: "哑音三角铁", 78: "开音三角铁", 79: "摇铃",
            80: "铃铛", 81: "响板"
        }
        return drum_names.get(note_number, f"音符{note_number}")

    def _start_play(self):
        """开始播放"""
        if not self.current_midi_file:
            messagebox.showwarning("提示", "请先选择MIDI文件")
            return
            
        try:
            def _do_start():
                self._log_message("开始架子鼓播放...")
                # 调用架子鼓控制器播放
                if hasattr(self.controller, 'start_from_file'):
                    tempo_var = self.app_ref.tempo_var if self.app_ref else self.tempo_var
                    tempo = tempo_var.get()
                    success = self.controller.start_from_file(self.current_midi_file, tempo=tempo)
                    if success:
                        self._log_message("播放已开始")
                        self._update_button_states(playing=True)
                        # 注册回调以在完成/停止时更新UI并联动播放列表
                        self._register_playback_callbacks()
                    else:
                        self._log_message("播放启动失败", "ERROR")
                else:
                    self._log_message("架子鼓控制器不可用", "ERROR")

            # 倒计时执行
            enable = bool(getattr(self, 'enable_countdown_var', tk.BooleanVar(value=True)).get())
            secs = int(getattr(self, 'countdown_seconds_var', tk.IntVar(value=3)).get())
            if enable and secs > 0:
                # 倒计时期间的特殊按钮状态：开始按钮禁用，暂停按钮启用
                self.btn_play.configure(text="开始演奏", state=tk.DISABLED, style='MF.Success.TButton')
                self.btn_pause.configure(text="暂停", state=tk.NORMAL, style='MF.Warning.TButton')
                self._countdown_remaining = secs
                def _tick():
                    rem = getattr(self, '_countdown_remaining', 0)
                    if rem <= 0:
                        self.countdown_label.configure(text="")
                        # 倒计时结束，恢复正常的按钮状态
                        self._update_button_states(playing=False)
                        self._countdown_after_id = None
                        _do_start()
                        return
                    self.countdown_label.configure(text=f"{rem} 秒后开始...")
                    self._countdown_remaining = rem - 1
                    self._countdown_after_id = self.countdown_label.after(1000, _tick)
                _tick()
            else:
                _do_start()
        
        except Exception as e:
            self._log_message(f"播放异常: {e}", "ERROR")

    def _pause_play(self):
        """暂停播放"""
        try:
            # 检查是否在倒计时期间
            if hasattr(self, '_countdown_after_id') and self._countdown_after_id:
                # 取消倒计时
                self.countdown_label.after_cancel(self._countdown_after_id)
                self._countdown_after_id = None
                self.countdown_label.configure(text="")
                # 恢复正常的按钮状态
                self._update_button_states(playing=False)
                self._log_message("倒计时已取消")
                return
            
            # 正常暂停逻辑
            if hasattr(self.controller, 'pause'):
                self.controller.pause()
                self._log_message("播放已暂停")
                self._update_button_states(paused=True)
        except Exception as e:
            self._log_message(f"暂停失败: {e}", "ERROR")

    def _stop_play(self):
        """停止播放"""
        try:
            # 取消倒计时（如果有）
            if hasattr(self, '_countdown_after_id') and self._countdown_after_id:
                self.countdown_label.after_cancel(self._countdown_after_id)
                self._countdown_after_id = None
                self.countdown_label.configure(text="")
            
            if hasattr(self.controller, 'stop'):
                self.controller.stop()
                self._log_message("播放已停止")
                self._update_button_states(playing=False)
        except Exception as e:
            self._log_message(f"停止失败: {e}", "ERROR")

    def _update_button_states(self, playing=False, paused=False):
        """更新按钮状态"""
        if not hasattr(self, 'btn_play') or not hasattr(self, 'btn_pause'):
            return
            
        if playing and not paused:
            self.btn_play.configure(text="开始演奏", state=tk.NORMAL, style='MF.Success.TButton')
            self.btn_pause.configure(text="暂停", state=tk.NORMAL, style='MF.Warning.TButton')
            if hasattr(self, 'btn_stop'):
                self.btn_stop.configure(state=tk.NORMAL)
        elif paused:
            self.btn_play.configure(text="开始演奏", state=tk.NORMAL, style='MF.Success.TButton')
            self.btn_pause.configure(text="恢复", state=tk.NORMAL, style='MF.Success.TButton')
            if hasattr(self, 'btn_stop'):
                self.btn_stop.configure(state=tk.NORMAL)
        else:
            self.btn_play.configure(text="开始演奏", state=tk.NORMAL, style='MF.Success.TButton')
            self.btn_pause.configure(text="暂停", state=tk.DISABLED, style='MF.Warning.TButton')
            if hasattr(self, 'btn_stop'):
                self.btn_stop.configure(state=tk.DISABLED)

    def _add_to_playlist(self, file_path):
        """添加文件到播放列表"""
        if not self.playlist_tree:
            return
        
        filename = os.path.basename(file_path)
        iid = self.playlist_tree.insert("", tk.END, text=filename, 
                                       values=("MIDI", "就绪"))
        self._playlist_paths[iid] = file_path

    def _load_midi_from_playlist(self, file_path):
        """从演奏列表加载MIDI文件到架子鼓页面"""
        try:
            if not file_path or not os.path.exists(file_path):
                self._log_message(f"文件不存在: {file_path}", "ERROR")
                return False
            
            # 设置当前MIDI文件
            self.current_midi_file = file_path
            
            # 更新主应用程序的文件路径变量
            if self.app_ref and hasattr(self.app_ref, 'midi_path_var'):
                self.app_ref.midi_path_var.set(file_path)
            
            # 更新文件信息显示
            if hasattr(self.app_ref, '_update_file_info_display'):
                self.app_ref._update_file_info_display(file_path)
            
            self._log_message(f"已加载MIDI文件到架子鼓页面: {os.path.basename(file_path)}", "SUCCESS")
            return True
            
        except Exception as e:
            self._log_message(f"加载MIDI文件失败: {e}", "ERROR")
            return False

    def _play_selected_from_playlist(self):
        try:
            if not self.playlist_tree:
                return
            sel = self.playlist_tree.selection()
            if not sel:
                # 若未选中，播放第一个
                first = self.playlist_tree.get_children()
                if not first:
                    return
                iid = first[0]
            else:
                iid = sel[0]
            path = self._playlist_paths.get(iid)
            if not path:
                return
            self.current_midi_file = path
            midi_path_var = self.app_ref.midi_path_var if self.app_ref else self.midi_path_var
            midi_path_var.set(path)
            self._current_playing_iid = iid
            self._mark_playlist_status(iid, "播放中")
            self._start_play()
        except Exception as e:
            self._log_message(f"播放所选失败: {e}", "ERROR")

    def _play_next_from_playlist(self):
        try:
            if not self.playlist_tree:
                return
            items = self.playlist_tree.get_children()
            if not items:
                return
            cur = self._current_playing_iid
            if cur not in items:
                idx = 0
            else:
                idx = items.index(cur) + 1
            if idx >= len(items):
                if self.playlist_mode_var.get() == '循环':
                    idx = 0
                else:
                    return
            iid = items[idx]
            path = self._playlist_paths.get(iid)
            if not path:
                return
            self.current_midi_file = path
            midi_path_var = self.app_ref.midi_path_var if self.app_ref else self.midi_path_var
            midi_path_var.set(path)
            self._current_playing_iid = iid
            self._mark_playlist_status(iid, "播放中")
            self._start_play()
        except Exception as e:
            self._log_message(f"下一首失败: {e}", "ERROR")

    def _play_prev_from_playlist(self):
        try:
            if not self.playlist_tree:
                return
            items = self.playlist_tree.get_children()
            if not items:
                return
            cur = self._current_playing_iid
            if cur not in items:
                idx = 0
            else:
                idx = max(0, items.index(cur) - 1)
            iid = items[idx]
            path = self._playlist_paths.get(iid)
            if not path:
                return
            self.current_midi_file = path
            midi_path_var = self.app_ref.midi_path_var if self.app_ref else self.midi_path_var
            midi_path_var.set(path)
            self._current_playing_iid = iid
            self._mark_playlist_status(iid, "播放中")
            self._start_play()
        except Exception as e:
            self._log_message(f"上一首失败: {e}", "ERROR")

    def _remove_selected_from_playlist(self):
        try:
            if not self.playlist_tree:
                return
            sel = self.playlist_tree.selection()
            for iid in sel:
                self.playlist_tree.delete(iid)
                self._playlist_paths.pop(iid, None)
                if self._current_playing_iid == iid:
                    self._current_playing_iid = None
        except Exception as e:
            self._log_message(f"移除失败: {e}", "ERROR")

    def _clear_playlist(self):
        try:
            if not self.playlist_tree:
                return
            for iid in list(self.playlist_tree.get_children()):
                self.playlist_tree.delete(iid)
            self._playlist_paths.clear()
            self._current_playing_iid = None
        except Exception as e:
            self._log_message(f"清空失败: {e}", "ERROR")

    def _mark_playlist_status(self, playing_iid: Optional[str], status: str):
        try:
            if not self.playlist_tree:
                return
            for iid in self.playlist_tree.get_children():
                vals = list(self.playlist_tree.item(iid, 'values'))
                if len(vals) < 2:
                    continue
                vals[1] = status if playing_iid and iid == playing_iid else ("就绪" if iid in self._playlist_paths else "")
                self.playlist_tree.item(iid, values=vals)
        except Exception:
            pass

    def _register_playback_callbacks(self):
        try:
            # 优先通过 app_ref 的 playback_service 注入回调
            svc = getattr(self.app_ref, 'playback_service', None)
            if svc and hasattr(svc, 'set_auto_callbacks'):
                svc.set_auto_callbacks(
                    on_complete=self._on_play_complete,
                    on_stop=self._on_play_stopped,
                    on_error=lambda msg=None: self._on_play_error(msg),
                )
                return
            # 回退：若 controller 支持 set_callbacks
            if hasattr(self.controller, 'set_callbacks'):
                try:
                    self.controller.set_callbacks(on_complete=self._on_play_complete,
                                                  on_stop=self._on_play_stopped)
                    return
                except Exception:
                    pass
        except Exception:
            pass

    def _on_play_complete(self):
        try:
            self._log_message("播放完成", "SUCCESS")
            self._update_button_states(playing=False)
            # 播放列表联动
            mode = self.playlist_mode_var.get()
            if mode in ("顺序", "循环"):
                self._play_next_from_playlist()
        except Exception:
            pass

    def _on_play_stopped(self):
        try:
            self._log_message("播放停止", "INFO")
            self._update_button_states(playing=False)
        except Exception:
            pass

    def _on_play_error(self, msg=None):
        try:
            self._log_message(f"播放错误: {msg}", "ERROR")
            self._update_button_states(playing=False)
        except Exception:
            pass

    def _log_message(self, message, level="INFO"):
        """记录日志消息"""
        if not self.log_text:
            return
        
        import time
        timestamp = time.strftime("%H:%M:%S")
        
        # 根据级别设置颜色
        color_map = {
            "INFO": "#000000",
            "SUCCESS": "#008000", 
            "WARNING": "#FF8C00",
            "ERROR": "#FF0000"
        }
        
        log_entry = f"[{timestamp}] {message}\n"
        
        self.log_text.insert(tk.END, log_entry)
        self.log_text.see(tk.END)
        
        # 限制日志长度
        lines = self.log_text.get("1.0", tk.END).split('\n')
        if len(lines) > 1000:
            self.log_text.delete("1.0", f"{len(lines)-500}.0")

    # ===== 定时和对时功能实现 =====
    
    def _timing_enable_network_clock(self):
        """启用网络时钟"""
        try:
            if not self.app_ref:
                self._log_message("应用引用不可用", "ERROR")
                return
            if not hasattr(self.app_ref, 'playback_controller'):
                self._log_message("播放控制器不可用", "ERROR")
                return
            if not self.app_ref.playback_controller:
                self._log_message("播放控制器未初始化", "ERROR")
                return
                
            # 通过播放控制器调用定时功能
            self.app_ref.playback_controller._timing_enable_network_clock()
            self._log_message("网络时钟已启用", "INFO")
        except Exception as e:
            self._log_message(f"启用网络时钟失败: {e}", "ERROR")


    def _timing_apply_servers(self):
        """应用NTP服务器设置"""
        try:
            if not self.app_ref:
                self._log_message("应用引用不可用", "ERROR")
                return
            if not hasattr(self.app_ref, 'playback_controller'):
                self._log_message("播放控制器不可用", "ERROR")
                return
            if not self.app_ref.playback_controller:
                self._log_message("播放控制器未初始化", "ERROR")
                return
                
            # 通过播放控制器调用定时功能
            self.app_ref.playback_controller._timing_apply_servers()
            self._log_message("NTP服务器设置已应用", "INFO")
        except Exception as e:
            self._log_message(f"应用NTP服务器失败: {e}", "ERROR")

    def _timing_toggle_ntp(self, enabled):
        """切换NTP启用状态"""
        try:
            if not self.app_ref:
                self._log_message("应用引用不可用", "ERROR")
                return
            if not hasattr(self.app_ref, 'playback_controller'):
                self._log_message("播放控制器不可用", "ERROR")
                return
            if not self.app_ref.playback_controller:
                self._log_message("播放控制器未初始化", "ERROR")
                return
                
            # 通过播放控制器调用定时功能
            self.app_ref.playback_controller._timing_toggle_ntp(enabled)
            if enabled:
                self._log_message("NTP后台对时已启用", "INFO")
            else:
                self._log_message("NTP已禁用，使用本地时钟", "INFO")
        except Exception as e:
            self._log_message(f"切换NTP状态失败: {e}", "ERROR")

    def _timing_set_resync_settings(self, interval, threshold):
        """设置对时参数"""
        try:
            if not self.app_ref:
                self._log_message("应用引用不可用", "ERROR")
                return
            if not hasattr(self.app_ref, 'playback_controller'):
                self._log_message("播放控制器不可用", "ERROR")
                return
            if not self.app_ref.playback_controller:
                self._log_message("播放控制器未初始化", "ERROR")
                return
                
            # 通过播放控制器调用定时功能
            self.app_ref.playback_controller._timing_set_resync_settings(interval, threshold)
            self._log_message(f"对时参数已更新: 间隔={interval}s, 阈值={threshold}ms", "INFO")
        except Exception as e:
            self._log_message(f"设置对时参数失败: {e}", "ERROR")

    def _timing_schedule_for_current_instrument(self):
        """为当前乐器创建定时计划"""
        try:
            if not self.current_midi_file:
                self._log_message("请先选择MIDI文件", "WARNING")
                return
            
            if not self.app_ref:
                self._log_message("应用引用不可用", "ERROR")
                return
            if not hasattr(self.app_ref, 'playback_controller'):
                self._log_message("播放控制器不可用", "ERROR")
                return
            if not self.app_ref.playback_controller:
                self._log_message("播放控制器未初始化", "ERROR")
                return
                
            # 通过播放控制器调用定时功能
            self.app_ref.playback_controller._timing_schedule_for_current_instrument()
            self._log_message("定时计划创建请求已发送", "INFO")
        except Exception as e:
            self._log_message(f"创建定时计划失败: {e}", "ERROR")

    def _timing_cancel_schedule(self):
        """取消定时计划"""
        try:
            if not self.app_ref:
                self._log_message("应用引用不可用", "ERROR")
                return
            if not hasattr(self.app_ref, 'playback_controller'):
                self._log_message("播放控制器不可用", "ERROR")
                return
            if not self.app_ref.playback_controller:
                self._log_message("播放控制器未初始化", "ERROR")
                return
                
            # 通过播放控制器调用定时功能
            self.app_ref.playback_controller._timing_cancel_schedule()
            self._log_message("定时计划取消请求已发送", "INFO")
        except Exception as e:
            self._log_message(f"取消定时计划失败: {e}", "ERROR")

    def _timing_test_now(self):
        """立即测试播放（按当前设置）"""
        try:
            if not self.current_midi_file:
                self._log_message("请先选择MIDI文件", "WARNING")
                return
            
            # 确保存在 playback_controller（供对时桥接使用）
            try:
                if not hasattr(self.controller, 'playback_controller') or getattr(self.controller, 'playback_controller', None) is None:
                    # 尝试从 app_ref 借用
                    if hasattr(self.app_ref, 'playback_controller') and getattr(self.app_ref, 'playback_controller', None):
                        setattr(self.controller, 'playback_controller', getattr(self.app_ref, 'playback_controller'))
                        print(f"[DEBUG] 架子鼓页面：从 app_ref 借用 playback_controller")
                    else:
                        # 兜底：尝试构造一个
                        try:
                            from meowauto.app.controllers.playback_controller import PlaybackController
                            pc = PlaybackController(self.app_ref or self.controller, getattr(self.app_ref or self.controller, 'playback_service', None))
                            setattr(self.controller, 'playback_controller', pc)
                            print(f"[DEBUG] 架子鼓页面：构造新的 playback_controller")
                        except Exception as e:
                            print(f"[DEBUG] 架子鼓页面：构造 playback_controller 失败: {e}")
                else:
                    print(f"[DEBUG] 架子鼓页面：playback_controller 已存在")
            except Exception as e:
                print(f"[DEBUG] 架子鼓页面：playback_controller 初始化异常: {e}")

            # 向 DrumsController 注入 app_ref，便于其通过 event_bus 发布播放事件
            try:
                if hasattr(self.controller, 'set_app_ref'):
                    self.controller.set_app_ref(self.app_ref)
                    print("[DEBUG] 架子鼓页面：已向 DrumsController 注入 app_ref")
            except Exception as e:
                print(f"[DEBUG] 架子鼓页面：注入 app_ref 到 DrumsController 失败: {e}")

            self.app_ref.playback_controller._timing_test_now()
            self._log_message("立即测试播放请求已发送", "INFO")
        except Exception as e:
            self._log_message(f"测试播放失败: {e}", "ERROR")

    def _timing_get_ui_status(self):
        """获取定时服务状态信息"""
        try:
            if not self.app_ref:
                return {}
            if not hasattr(self.app_ref, 'playback_controller'):
                return {}
            if not self.app_ref.playback_controller:
                return {}
                
            # 通过播放控制器获取定时状态
            return self.app_ref.playback_controller._timing_get_ui_status()
        except Exception as e:
            self._log_message(f"获取定时状态失败: {e}", "ERROR")
        return {}

    def _refresh_timing_status(self, delay_ms: int = 0):
        """刷新定时状态显示"""
        def _do():
            try:
                st = self._timing_get_ui_status() or {}
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
                self.timing_status_var.set("\n".join(lines))
            except Exception:
                pass
            # 循环刷新
            try:
                if hasattr(self, 'app_ref') and self.app_ref and hasattr(self.app_ref, 'root'):
                    self.app_ref.root.after(1000, self._refresh_timing_status)
            except Exception:
                pass
        try:
            if hasattr(self, 'app_ref') and self.app_ref and hasattr(self.app_ref, 'root'):
                self.app_ref.root.after(delay_ms or 0, _do)
            else:
                _do()
        except Exception:
            _do()

    def _play_midi(self):
        """播放MIDI音频（纯音频播放，不触发自动演奏）"""
        try:
            if not self.current_midi_file:
                messagebox.showerror("错误", "请先选择MIDI文件")
                return
            
            # 使用app_ref的播放方法
            if self.app_ref and hasattr(self.app_ref, '_play_midi'):
                self.app_ref._play_midi()
            else:
                self._log_message("MIDI播放功能不可用", "ERROR")
        except Exception as e:
            self._log_message(f"播放MIDI音频失败: {e}", "ERROR")

    def _stop_playback(self):
        """停止所有播放"""
        try:
            # 停止架子鼓播放
            if hasattr(self.controller, 'stop'):
                self.controller.stop()
            
            # 使用app_ref的停止方法
            if self.app_ref and hasattr(self.app_ref, '_stop_playback'):
                self.app_ref._stop_playback()
            
            # 更新按钮状态
            self._update_button_states(playing=False)
            if hasattr(self, 'midi_play_button'):
                self.midi_play_button.configure(text="播放MIDI音频", style='MF.Info.TButton')
            
            self._log_message("所有播放已停止")
        except Exception as e:
            self._log_message(f"停止播放失败: {e}", "ERROR")

    def _update_schedule_button_state(self):
        """更新计划按钮状态（根据实际计划状态）"""
        try:
            if not hasattr(self, 'schedule_button'):
                return
            
            # 检查播放控制器是否有活跃的计划
            has_schedule = False
            try:
                if (self.app_ref and hasattr(self.app_ref, 'playback_controller') and 
                    self.app_ref.playback_controller and 
                    hasattr(self.app_ref.playback_controller, '_last_schedule_id') and
                    self.app_ref.playback_controller._last_schedule_id):
                    has_schedule = True
            except Exception:
                pass
            
            if has_schedule:
                self.schedule_button.configure(text="取消计划", style='MF.Danger.TButton')
            else:
                self.schedule_button.configure(text="创建计划", style='MF.Success.TButton')
        except Exception as e:
            self._log_message(f"更新计划按钮状态失败: {e}", "ERROR")
