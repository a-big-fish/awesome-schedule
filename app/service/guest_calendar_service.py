"""Synthetic schedules only; never read account data for the guest calendar."""
from datetime import datetime, timedelta
from zoneinfo import ZoneInfo
from app.mapper.memory_personal_event_mapper import MemoryPersonalEventMapper
from app.mapper.memory_my_resume_mapper import MemoryMyResumeMapper
from app.model.personal_event import PersonalEvent
from app.service.personal_schedule_service import PersonalScheduleService


def build_guest_schedule():
    mapper = MemoryPersonalEventMapper()
    today = datetime.now(ZoneInfo("Asia/Shanghai")).replace(hour=0, minute=0, second=0, microsecond=0)
    examples = [("personal", "整理求职目标", 8, 1), ("course", "学习数据库索引", 10, 2),
                ("interview", "后端开发模拟面试", 14, 1), ("meeting", "项目复盘会议", 16, 1),
                ("exam", "算法练习", 19, 1), ("recruiting", "校园招聘分享会", 20, 1)]
    for day in range(-3, 8):
        for index, (kind, title, hour, duration) in enumerate(examples):
            start = today + timedelta(days=day, hours=hour)
            mapper.save(PersonalEvent(id=f"demo-{day}-{index}", owner_id="__guest__", type=kind,
                title=f"[示例] {title}", start_at=start, end_at=start + timedelta(hours=duration),
                location_text="示例地点", note="仅用于体验，非真实用户数据。",
                completed_at=start if index == 0 else None))
    mapper.save(PersonalEvent(id="demo-multiday", owner_id="__guest__", type="personal",
        title="[示例] 完善项目作品集", start_at=today-timedelta(days=1)+timedelta(hours=9),
        end_at=today+timedelta(days=2, hours=18)))
    return PersonalScheduleService(mapper, MemoryMyResumeMapper(), owner_id="__guest__")
