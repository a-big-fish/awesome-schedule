using System;
using System.Text.Json;

namespace AwesomeInterview.NativeDemo.Data;

public sealed partial class WorkspaceRepository
{
    bool HasValidReceiptBody(SyncQueueItem item,JsonElement reply,JsonElement payload)
    {
        if(item.Endpoint==SyncQueueRules.EventEndpoint){
            var command=ReadEventCommand(item);if(command==null)return false;
            if(command.Action=="delete")return reply.TryGetProperty("deleted_id",out var deleted)&&deleted.ValueKind==JsonValueKind.String&&deleted.GetString()==item.EntityId;
            if(payload.ValueKind!=JsonValueKind.Object)return false;
            var value=ReadEventPayload(payload,Current!.UserId);
            if(value==null||value.Id!=item.EntityId||string.IsNullOrWhiteSpace(value.Title)||value.EndAt<=value.StartAt)return false;
            try{_ = value.Zone;}catch(TimeZoneNotFoundException){return false;}catch(InvalidTimeZoneException){return false;}
            return true;
        }
        if(item.Endpoint!=SyncQueueRules.IdeaEndpoint||payload.ValueKind!=JsonValueKind.Object)return false;
        try{var value=payload.Deserialize<IdeaSyncPayload>(Wire.Json);return value!=null&&value.Id==item.EntityId&&!string.IsNullOrWhiteSpace(value.Title)&&value.Status is "pending" or "abandoned";}
        catch(JsonException){return false;}
    }
}
