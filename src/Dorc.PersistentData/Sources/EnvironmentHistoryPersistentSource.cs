﻿using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Environment = Dorc.PersistentData.Model.Environment;
using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Contexts;

namespace Dorc.PersistentData.Sources
{
    public class EnvironmentHistoryPersistentSource : IEnvironmentHistoryPersistentSource
    {
        private readonly ILogger _logger;
        private readonly IDeploymentContextFactory _contextFactory;

        public EnvironmentHistoryPersistentSource(ILogger logger, IDeploymentContextFactory contextFactory)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        public bool UpdateHistory(string envName, string backupFile, string comment, string updatedBy,
            string updateType)
        {
            try
            {
                _logger.LogInformation(
                    $"Updating the Environment and EnvironmentHistory tables for environment {envName}");
                _logger.LogInformation("Here...");

                using (var context = _contextFactory.GetContext())
                {
                    AddHistory(envName, backupFile, comment, updatedBy, updateType, context);
                    context.SaveChanges();
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogInformation($"Error occurred updating the Env Mgt database for environment {envName}");
                _logger.LogInformation($"Error message:  {ex.Message}. ");
                return false;
            }
        }

        internal static void AddHistory(Environment environment, string backupFile, string newVersion, string updatedBy, string updateType,
            IDeploymentContext context)
        {
            var firstEnvHistory = context.EnvironmentHistories.Include(h => h.Environment)
                .Where(h => h.Environment.Name == environment.Name)
                .OrderByDescending(h => h.Id).FirstOrDefault();

            var oldBackupFile = firstEnvHistory != null ? firstEnvHistory.ToValue : string.Empty;
            var newBackupFile = backupFile != string.Empty ? backupFile : oldBackupFile;
            var newHistory = new EnvironmentHistory
            {
                Environment = environment,
                UpdateDate = DateTime.Now,
                UpdateType = updateType,
                UpdatedBy = updatedBy,
                FromValue = oldBackupFile,
                ToValue = newBackupFile,
                Details = newVersion
            };
            context.EnvironmentHistories.Add(newHistory);
            environment.RestoredFromBackup = newBackupFile;
            environment.LastUpdate = newHistory.UpdateDate;
        }

        internal static void AddHistory(string envName, string backupFile, string newVersion, string updatedBy, string updateType,
            IDeploymentContext context)
        {
            var firstEnvHistory = context.EnvironmentHistories.Include(h => h.Environment)
                .Where(h => h.Environment.Name == envName)
                .OrderByDescending(h => h.Id).FirstOrDefault();

            var oldBackupFile = firstEnvHistory != null ? firstEnvHistory.ToValue : string.Empty;
            var newBackupFile = backupFile != string.Empty ? backupFile : oldBackupFile;
            var envDetails = EnvironmentUnifier.GetEnvironment(context, envName);
            var newHistory = new EnvironmentHistory
            {
                Environment = envDetails,
                UpdateDate = DateTime.Now,
                UpdateType = updateType,
                UpdatedBy = updatedBy,
                FromValue = oldBackupFile,
                ToValue = newBackupFile,
                Details = newVersion
            };
            context.EnvironmentHistories.Add(newHistory);
            envDetails.RestoredFromBackup = newBackupFile;
            envDetails.LastUpdate = newHistory.UpdateDate;
        }

        internal static void AddDeletionHistory(string newVersion, string updatedBy, string updateType,
            IDeploymentContext context)
        {
            var newHistory = new EnvironmentHistory
            {
                EnvId = null, // Explicitly set to null for deletion records
                Environment = null,
                UpdateDate = DateTime.Now,
                UpdateType = updateType,
                UpdatedBy = updatedBy,
                ToValue = newVersion
            };
            context.EnvironmentHistories.Add(newHistory);
        }

        public List<EnvironmentHistoryApiModel> GetEnvironmentDetailHistory(int envId)
        {
            using (var context = _contextFactory.GetContext())
            {
                var result = context.EnvironmentHistories
                    .Include(h => h.Environment)
                    .Where(e => e.EnvId == envId || (e.Environment != null && e.Environment.Id == envId))
                    .Select(MapToEnvironmentHistoryApiModel).ToList();
                return result;
            }
        }


        public void UpdateEnvironmentDetailHistoryComment(int id, string comment)
        {
            using (var context = _contextFactory.GetContext())
            {
                var result = context.EnvironmentHistories
                    .Where(environmentHistory => environmentHistory.Id == id)
                    .ExecuteUpdate(setters =>
                        setters.SetProperty(environmentHistory =>
                            environmentHistory.Comment, comment));
            }
        }

        EnvironmentHistoryApiModel MapToEnvironmentHistoryApiModel(EnvironmentHistory h)
        {
            return new EnvironmentHistoryApiModel
            {
                Comment = h.Comment,
                Id = h.Id,
                EnvName = h.Environment?.Name ?? "DELETED ENVIRONMENT", // Handle deleted environments
                FromValue = h.FromValue,
                ToValue = h.ToValue,
                Details = h.Details,
                UpdateDate = h.UpdateDate.ToString(),
                UpdatedBy = h.UpdatedBy,
                UpdateType = h.UpdateType
            };
        }
    }
}