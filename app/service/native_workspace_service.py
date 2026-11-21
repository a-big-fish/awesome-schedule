"""Native documents use the existing parser/exporter; no HTML scraping or external PDF process."""
import base64
from datetime import datetime
from zoneinfo import ZoneInfo

import pymupdf

from app.model.my_resume_profile import MyResumeProfile
from app.service.interview_experience_export import export_pdf


class NativeWorkspaceService:
    @staticmethod
    def interview_list(schedule, query: str, status: str, order: str, page: int) -> dict:
        now = datetime.now(ZoneInfo("Asia/Shanghai"))
        items = [e for e in schedule.list_events() if e.type == "interview"]
        needle = query.strip().casefold()
        items = [e for e in items if not needle or needle in f"{e.title} {e.position or ''} {e.note or ''}".casefold()]
        if status == "upcoming":
            items = [e for e in items if e.end_at > now and not e.cancelled_at and not e.completed_at]
        elif status == "finished":
            items = [e for e in items if e.end_at <= now or e.completed_at]
        items.sort(key=lambda e: (e.start_at, e.id), reverse=order != "oldest")
        pages = max(1, (len(items) + 19) // 20)
        page = min(max(1, page), pages)
        return {"items": [e.model_dump(mode="json", exclude={"raw_invitation"}) for e in items[(page-1)*20:page*20]],
                "total": len(items), "page": page, "page_count": pages}

    @staticmethod
    async def upload_resume(upload, schedule, profiles, parser, state) -> dict:
        parsed = await parser.parse_upload(upload, owner_id=schedule.owner_id)
        profiles.save(MyResumeProfile(id=schedule.profile_id, resume_id=parsed.id))
        return {"resume": state.current()}

    @staticmethod
    def resume_document(state, parser) -> dict:
        current = state.current()
        if current is None:
            raise LookupError("尚未上传简历")
        resume = parser.resume_mapper.find_summary(current["id"])
        if not resume or resume.owner_id != state.owner_id:
            raise LookupError("简历不存在")
        content = parser.read_pdf(resume.id, owned_resume=resume)
        from app.core.postgres_pool import release_shared_connection
        release_shared_connection()
        return {**current, **NativeWorkspaceService.render_pdf(content)}

    @staticmethod
    def experience_document(service, experience_id, style, title, hide_company, hide_position,
                            hide_interviewed_at, hide_on_site_answer, hide_review_answer, hide_interviewer_answer):
        original = service.get_experience(experience_id)
        if original is None:
            raise LookupError("面经不存在")
        item = original.model_copy(deep=True)
        if style not in {"lines", "flat"}:
            raise ValueError("导出样式无效")
        if len(title.strip()) > 200:
            raise ValueError("导出标题不能超过200个字符")
        if title.strip():
            item.title = title.strip()
        if hide_company:
            item.company = None
        if hide_position:
            item.position = None
        if hide_interviewed_at:
            item.interviewed_at = None
        from app.core.postgres_pool import release_shared_connection
        rows = service.numbered_questions(experience_id)
        release_shared_connection()
        content = export_pdf(item, rows, style,
                             hide_on_site_answer=hide_on_site_answer, hide_review_answer=hide_review_answer,
                             hide_interviewer_answer=hide_interviewer_answer)
        return NativeWorkspaceService.render_pdf(content)

    @staticmethod
    def render_pdf(content: bytes) -> dict:
        """Bounded image pages are displayed by native WPF, and can be encrypted for offline reading."""
        pages = []
        size = 0
        try:
            document = pymupdf.open(stream=content, filetype="pdf")
        except (pymupdf.FileDataError, RuntimeError, ValueError) as error:
            raise ValueError("无法预览此PDF，请确认文件没有损坏") from error
        with document:
            if document.needs_pass or not 0 < document.page_count <= 80:
                raise ValueError("仅支持未加密、1至80页的PDF预览")
            for page in document:
                if (page.rect.width > 3000 or page.rect.height > 5000
                        or page.rect.width * page.rect.height * 1.8 ** 2 > 8_000_000):
                    raise ValueError("PDF页面尺寸过大")
                pix = page.get_pixmap(matrix=pymupdf.Matrix(1.8, 1.8), alpha=False)
                png = pix.tobytes("png")
                size += len(png)
                if size > 40 * 1024 * 1024:
                    raise ValueError("预览超过40MB，请减少页数或下载原始PDF")
                pages.append(base64.b64encode(png).decode("ascii"))
        return {"pages": pages}
