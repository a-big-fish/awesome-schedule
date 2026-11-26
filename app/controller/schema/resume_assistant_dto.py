from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field, SecretStr, StringConstraints, field_validator, model_validator


ShortText = Annotated[str, StringConstraints(strip_whitespace=True, max_length=200)]
TurnText = Annotated[str, StringConstraints(strip_whitespace=True, min_length=1, max_length=3000)]


class PracticeTurn(BaseModel):
    model_config = ConfigDict(extra="forbid")

    role: Literal["user", "assistant"]
    content: TurnText


class ResumeAssistantInput(BaseModel):
    model_config = ConfigDict(extra="forbid")

    version: Annotated[str, StringConstraints(min_length=1, max_length=128)]
    action: Literal["start", "answer", "review", "optimize"]
    stream_suggestions: bool = False
    position: ShortText = ""
    focus: ShortText = ""
    reference_experiences: bool = False
    history: list[PracticeTurn] = Field(default_factory=list, max_length=12)
    answer: Annotated[str, StringConstraints(strip_whitespace=True, max_length=3000)] = ""
    model_ids: list[Annotated[str, StringConstraints(min_length=1, max_length=64)]] | None = Field(
        default=None, min_length=1, max_length=2,
    )

    @model_validator(mode="after")
    def validate_model_selection(self):
        if self.stream_suggestions and self.model_ids is None:
            raise ValueError("逐条输出需要选择分析模型。")
        if self.model_ids is not None and (
            self.action != "optimize" or len(set(self.model_ids)) != len(self.model_ids)
        ):
            raise ValueError("简历优化只接受不同的模型配置。")
        return self


class ResumeModelCredentials(BaseModel):
    model_config = ConfigDict(extra="forbid", hide_input_in_errors=True)

    base_url: Annotated[str, StringConstraints(strip_whitespace=True, min_length=1, max_length=2048)]
    api_key: SecretStr

    @field_validator("api_key")
    @classmethod
    def validate_api_key(cls, secret: SecretStr) -> SecretStr:
        key = secret.get_secret_value().strip()
        if not 0 < len(key) <= 4096 or not all(33 <= ord(char) <= 126 for char in key):
            raise ValueError("请填写有效的 API Key。")
        return SecretStr(key)


class ResumeModelInput(ResumeModelCredentials):
    name: Annotated[str, StringConstraints(strip_whitespace=True, max_length=40)] = ""
    model: Annotated[str, StringConstraints(strip_whitespace=True, min_length=1, max_length=200)]
