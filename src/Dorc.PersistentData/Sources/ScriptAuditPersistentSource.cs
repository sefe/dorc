using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using log4net;
using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Contexts;

namespace Dorc.PersistentData.Sources
{
    public class ScriptAuditPersistentSource : IScriptAuditPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;
        private readonly ILog _logger;

        public ScriptAuditPersistentSource(
            IDeploymentContextFactory contextFactory,
            ILog logger
            )
        {
            _logger = logger;
            _contextFactory = contextFactory;
        }

        public void InsertScriptAudit(string username, ActionType actionType, ScriptApiModel scriptApiModel)
        {
            using (var context = _contextFactory.GetContext())
            {
                // Ensure the action type exists, create if it doesn't
                var action = context.ScriptAuditActions.FirstOrDefault(x => x.Action == actionType);
                if (action == null)
                {
                    action = new Model.ScriptAuditAction { Action = actionType };
                    context.ScriptAuditActions.Add(action);
                    context.SaveChanges();
                }

                var scriptAudit = new ScriptAudit
                {
                    Date = DateTime.Now,
                    Username = username,
                    ScriptId = scriptApiModel.Id,
                    Json = JsonSerializer.Serialize(scriptApiModel, new JsonSerializerOptions { WriteIndented = true }),
                    Action = action
                };

                context.ScriptAudits.Add(scriptAudit);
                context.SaveChanges();
            }
        }
    }
}