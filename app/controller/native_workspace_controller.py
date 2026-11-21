"""JSON adapters for the native interview, resume and note editing workspace."""
from fastapi import APIRouter, Depends, HTTPException, Response, UploadFile
from app.controller.schema.native_workspace_dto import NativeDraftInput
from app.core.dependencies import (get_interview_experience_service, get_personal_schedule_service,
    get_my_resume_mapper, get_resume_parser_service, get_personal_resume_state_service)
from app.core.exceptions import ResumeParseError, ResumeFileNotFoundError
from app.service.native_workspace_service import NativeWorkspaceService

router = APIRouter(prefix="/workspace", tags=["native-workspace"])


def fail(error: Exception) -> HTTPException:
    return HTTPException(404 if isinstance(error, LookupError) else 409 if isinstance(error, RuntimeError) else 422, str(error))


@router.get("/interviews")
def interviews(q: str = "", status: str = "all", order: str = "newest", page: int = 1,
               schedule=Depends(get_personal_schedule_service)):
    return NativeWorkspaceService.interview_list(schedule, q[:100], status, order, page)


@router.get("/resume")
def current_resume(response: Response, state=Depends(get_personal_resume_state_service)):
    response.headers["Cache-Control"] = "no-store"
    return {"resume": state.current()}


@router.post("/resume", status_code=201)
async def upload_resume(resume: UploadFile, schedule=Depends(get_personal_schedule_service),
                        profiles=Depends(get_my_resume_mapper), parser=Depends(get_resume_parser_service),
                        state=Depends(get_personal_resume_state_service)):
    try:
        return await NativeWorkspaceService.upload_resume(resume, schedule, profiles, parser, state)
    except ResumeParseError as error:
        raise HTTPException(422, str(error)) from error


@router.get("/resume/document")
def resume_document(response: Response, state=Depends(get_personal_resume_state_service),
                    parser=Depends(get_resume_parser_service)):
    response.headers["Cache-Control"] = "no-store"
    try:
        return NativeWorkspaceService.resume_document(state, parser)
    except ResumeFileNotFoundError as error:
        raise HTTPException(404, "简历文件不可用，请重新上传") from error
    except (LookupError, ValueError) as error:
        raise fail(error) from error


@router.put("/experiences/{experience_id}/draft")
def draft(experience_id: str, payload: NativeDraftInput, service=Depends(get_interview_experience_service)):
    try:
        item, questions = service.save_draft(experience_id, payload.version, payload.questions,
            payload.category_minutes, payload.total_minutes, payload.feeling, payload.raw_text_draft)
        return {"experience": {**item.model_dump(mode="json"),
                               "questions": [q.model_dump(mode="json") for q in questions]}}
    except (LookupError, RuntimeError, ValueError, TypeError) as error:
        raise fail(error) from error


@router.delete("/experiences/{experience_id}", status_code=204)
def delete(experience_id: str, service=Depends(get_interview_experience_service)):
    if not service.delete_experience(experience_id):
        raise HTTPException(404, "面经不存在")


@router.get("/experiences/{experience_id}/document")
def experience_document(experience_id: str, response: Response, style: str = "lines", title: str = "",
                        hide_company: bool = False, hide_position: bool = False, hide_interviewed_at: bool = False,
                        hide_on_site_answer: bool = False, hide_review_answer: bool = False,
                        hide_interviewer_answer: bool = False, service=Depends(get_interview_experience_service)):
    response.headers["Cache-Control"] = "no-store"
    try:
        return NativeWorkspaceService.experience_document(service, experience_id, style, title,
            hide_company, hide_position, hide_interviewed_at, hide_on_site_answer, hide_review_answer, hide_interviewer_answer)
    except (LookupError, ValueError) as error:
        raise fail(error) from error
