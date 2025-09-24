#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Drums 控制器：负责协调架子鼓页面与后端服务。
新增：桥接 AutoPlayer 回调到 App.event_bus，从而让 UI 按钮（开始/暂停/停止）
在定时触发或直接调用时都能自动联动。
"""
from __future__ import annotations
from typing import Any, Optional, Dict
import threading
import time

try:
    # 事件常量，用于发布播放开始/暂停/恢复/停止等事件
    from app.event_bus import Events  # type: ignore
except Exception:
    class Events:  # 回退占位，避免导入失败
        PLAYBACK_START = "playback.start"
        PLAYBACK_STOP = "playback.stop"
        PLAYBACK_PAUSE = "playback.pause"
        PLAYBACK_RESUME = "playback.resume"
        PLAYBACK_COMPLETE = "playback.complete"
        PLAYBACK_ERROR = "playback.error"

try:
    from meowauto.app.services.playback_service import PlaybackService
except Exception:
    PlaybackService = None  # 运行期兜底
try:
    from meowauto.playback.keymaps_ext.drums import DRUMS_KEYMAP
except Exception:
    DRUMS_KEYMAP = {}


class DrumsController:
    """架子鼓控制器：负责页面到播放服务的桥接。"""

    def __init__(self, service: Optional[Any] = None, app_ref: Optional[Any] = None):
        self.service: Any = service or (PlaybackService() if PlaybackService else None)
        self.settings: Dict[str, Any] = {
            'tempo': 1.0,
            'key_mapping': dict(DRUMS_KEYMAP),  # 可被 UI 覆盖
            # 预留：量化/最短持续/连击合并等
        }
        self.current_instrument = "架子鼓"  # 用于定时服务识别
        # App 引用（用于发布事件）
        self.app_ref: Optional[Any] = app_ref

    # 供页面在挂载时注入 app 引用
    def set_app_ref(self, app_ref: Any) -> None:
        self.app_ref = app_ref

    def _publish(self, event_name: str, payload: Optional[Dict[str, Any]] = None) -> None:
        """通过 App.event_bus 发布事件（若可用）。"""
        try:
            if self.app_ref and hasattr(self.app_ref, 'event_bus') and self.app_ref.event_bus:
                self.app_ref.event_bus.publish(event_name, payload or {}, 'DrumsController')
        except Exception:
            pass

    def _start_playback_monitor(self) -> None:
        """后台监视 AutoPlayer 的播放状态，播放结束时发布 STOP/COMPLETE。"""
        if not self.service:
            return
        try:
            ap = getattr(self.service, 'auto_player', None)
            if not ap:
                return

            def _monitor():
                try:
                    # 最长监视2小时，避免异常泄漏
                    deadline = time.time() + 2 * 60 * 60
                    was_playing = True
                    while time.time() < deadline:
                        try:
                            playing = bool(getattr(ap, 'is_playing', False))
                        except Exception:
                            playing = False
                        if not playing:
                            # 尝试区分正常完成与外部停止：AutoPlayer若有标志可读取，否则统一发 STOP
                            self._publish(Events.PLAYBACK_STOP, {'instrument': '架子鼓'})
                            # 可选：也发 COMPLETE 以便界面收尾
                            self._publish(Events.PLAYBACK_COMPLETE, {'instrument': '架子鼓'})
                            break
                        time.sleep(0.2)
                except Exception:
                    pass

            t = threading.Thread(target=_monitor, name="DrumsPlaybackMonitor", daemon=True)
            t.start()
        except Exception:
            pass

    def _wire_auto_callbacks(self) -> None:
        """将 AutoPlayer 的生命周期事件回调桥接到 EventBus。"""
        if not self.service:
            return
        try:
            def _on_start():
                self._publish(Events.PLAYBACK_START, {'instrument': '架子鼓'})

            def _on_pause():
                self._publish(Events.PLAYBACK_PAUSE, {'instrument': '架子鼓'})

            def _on_resume():
                self._publish(Events.PLAYBACK_RESUME, {'instrument': '架子鼓'})

            def _on_stop():
                self._publish(Events.PLAYBACK_STOP, {'instrument': '架子鼓'})

            def _on_complete():
                self._publish(Events.PLAYBACK_COMPLETE, {'instrument': '架子鼓'})

            def _on_error(msg: str):
                self._publish(Events.PLAYBACK_ERROR, {'instrument': '架子鼓', 'message': msg})

            if hasattr(self.service, 'set_auto_callbacks'):
                self.service.set_auto_callbacks(
                    on_start=_on_start,
                    on_pause=_on_pause,
                    on_resume=_on_resume,
                    on_stop=_on_stop,
                    on_complete=_on_complete,
                    on_error=_on_error,
                )
        except Exception:
            pass

    # 兼容旧接口
    def start(self):
        return False

    def start_from_file(self, midi_path: str, *, tempo: Optional[float] = None, key_mapping: Optional[Dict[str, str]] = None) -> bool:
        if not midi_path:
            return False
        if not self.service:
            return False
        try:
            # 确保播放器初始化
            if hasattr(self.service, 'init_players'):
                self.service.init_players()
            # 桥接 AutoPlayer 回调 -> 事件总线
            self._wire_auto_callbacks()
            ap = getattr(self.service, 'auto_player', None)
            if not ap or not hasattr(ap, 'start_auto_play_midi_drums'):
                return False
            t = float(tempo) if tempo is not None else float(self.settings.get('tempo', 1.0))
            km = key_mapping or self.settings.get('key_mapping') or DRUMS_KEYMAP
            ok = bool(ap.start_auto_play_midi_drums(midi_path, tempo=t, key_mapping=km))
            # 若AutoPlayer未触发回调，保底发布开始事件
            if ok:
                self._publish(Events.PLAYBACK_START, {'instrument': '架子鼓'})
                # 启动后台监视，确保结束时能发布STOP/COMPLETE
                self._start_playback_monitor()
            return ok
        except Exception:
            return False

    def stop(self) -> None:
        if not self.service:
            return
        try:
            if hasattr(self.service, 'stop_auto_only'):
                self.service.stop_auto_only()
            # 保底发布停止事件
            self._publish(Events.PLAYBACK_STOP, {'instrument': '架子鼓'})
        except Exception:
            pass

    def pause(self) -> None:
        if not self.service:
            return
        try:
            if hasattr(self.service, 'pause_auto_only'):
                self.service.pause_auto_only()
            # 保底发布暂停事件
            self._publish(Events.PLAYBACK_PAUSE, {'instrument': '架子鼓'})
        except Exception:
            pass

    def resume(self) -> None:
        if not self.service:
            return
        try:
            if hasattr(self.service, 'resume_auto_only'):
                self.service.resume_auto_only()
            # 保底发布恢复事件
            self._publish(Events.PLAYBACK_RESUME, {'instrument': '架子鼓'})
        except Exception:
            pass

    def apply_settings(self, settings: dict):
        if not isinstance(settings, dict):
            return
        try:
            if 'tempo' in settings:
                self.settings['tempo'] = float(settings['tempo'])
        except Exception:
            pass
        try:
            km = settings.get('key_mapping')
            if isinstance(km, dict) and km:
                self.settings['key_mapping'] = km
        except Exception:
            pass
