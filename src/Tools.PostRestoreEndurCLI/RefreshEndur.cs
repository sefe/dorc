using System;
using System.Collections.Generic;
using System.Linq;
using Dorc.PersistentData.Repositories;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;

namespace Tools.PostRestoreEndurCLI
{
    public class RefreshEndur
    {
        private readonly ILogger _logger;
        private readonly ISqlPortsPersistentSource _sqlPortsPersistentSource;
        private readonly IDatabasesPersistentSource _databasesPersistentSource;
        private readonly IServersPersistentSource _serversPersistentSource;

        public RefreshEndur(ILogger logger, ISqlPortsPersistentSource sqlPortsPersistentSource, IDatabasesPersistentSource databasesPersistentSource, IServersPersistentSource serversPersistentSource)
        {
            _serversPersistentSource = serversPersistentSource;
            _databasesPersistentSource = databasesPersistentSource;
            _sqlPortsPersistentSource = sqlPortsPersistentSource;
            _logger = logger;
        }

        public bool UpdateAppServerDetails(string envName, string strSVCAccount)
        {
            try
            {
                var endurAppServers = _serversPersistentSource.GetAppServerDetails(envName);
                var targetDB = _databasesPersistentSource.GetDatabaseByType(envName, "Endur");

                var SQLConStr = new SqlConnectionStringBuilder
                {
                    DataSource = targetDB.ServerName,
                    IntegratedSecurity = true,
                    Pooling = false,
                    InitialCatalog = targetDB.Name,
                    TrustServerCertificate = true
                };
                var SQLConnection = new SqlConnection(SQLConStr.ConnectionString);
                Output("Connection: " + SQLConStr.ConnectionString);
                SQLConnection.Open();

                var strSQL = "SELECT * FROM [dbo].[service_mgr]";
                var endurServiceManagerList = new List<EndurServiceManager>();

                using (var sqlCmd = new SqlCommand(strSQL, SQLConnection))
                {
                    using (var reader = sqlCmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var serviceManager = new EndurServiceManager
                            {
                                id = reader.GetInt32(0),
                                service_name = reader.GetString(1),
                                login_name = reader.GetString(2),
                                workstation_name = reader.GetString(3),
                                default_service = reader.GetInt32(4),
                                app_login_name = reader.GetString(5),
                                curr_date = reader.GetDateTime(6),
                                user_id = reader.GetInt32(7)
                            };
                            endurServiceManagerList.Add(serviceManager);
                        }
                    }
                }


                foreach (var endurServiceManagerRow in endurServiceManagerList)
                {
                    Output(endurServiceManagerRow.id + ";" + endurServiceManagerRow.service_name + ";" +
                           endurServiceManagerRow.login_name + ";" + endurServiceManagerRow.workstation_name + ";" +
                           endurServiceManagerRow.default_service + ";" + endurServiceManagerRow.app_login_name + ";" +
                           endurServiceManagerRow.curr_date + ";" + endurServiceManagerRow.user_id);
                    var result = endurAppServers.SingleOrDefault(x =>
                        x.ApplicationTags.Contains(endurServiceManagerRow.app_login_name));
                    if (result != null)
                    {
                        endurServiceManagerRow.login_name = strSVCAccount;
                        endurServiceManagerRow.workstation_name = result.Name;
                    }
                    else
                    {
                        endurServiceManagerRow.login_name = "NA";
                        endurServiceManagerRow.workstation_name = "NA";
                    }

                    using (var insertCommand =
                        new SqlCommand(
                            "EXEC dbo.update_service_mgr @id, @service_name, @login_name, @workstation_name, @default_service, @app_login_name, @curr_date, @user_id",
                            SQLConnection))
                    {
                        Output(endurServiceManagerRow.id + ";" + endurServiceManagerRow.service_name + ";" +
                               endurServiceManagerRow.login_name + ";" + endurServiceManagerRow.workstation_name + ";" +
                               endurServiceManagerRow.default_service + ";" + endurServiceManagerRow.app_login_name +
                               ";" + endurServiceManagerRow.curr_date + ";" + endurServiceManagerRow.user_id);
                        insertCommand.Parameters.AddWithValue("@id", endurServiceManagerRow.id);
                        insertCommand.Parameters.AddWithValue("@service_name", endurServiceManagerRow.service_name);
                        insertCommand.Parameters.AddWithValue("@login_name", endurServiceManagerRow.login_name);
                        insertCommand.Parameters.AddWithValue("@workstation_name",
                            endurServiceManagerRow.workstation_name);
                        insertCommand.Parameters.AddWithValue("@default_service",
                            endurServiceManagerRow.default_service);
                        insertCommand.Parameters.AddWithValue("@app_login_name", endurServiceManagerRow.app_login_name);
                        insertCommand.Parameters.AddWithValue("@curr_date", endurServiceManagerRow.curr_date);
                        insertCommand.Parameters.AddWithValue("@user_id", endurServiceManagerRow.user_id);
                        insertCommand.ExecuteNonQuery();
                    }
                }

                SQLConnection.Close();
                return true;
            }
            catch (Exception ex)
            {
                Output($"Error occurred updating the App Server Details for Environment : {envName}");
                Output($"Error message:  {ex.Message}. ");
                Output($"Error message:  {ex.InnerException}. ");
                return false;
            }
        }

        public bool UpdateUserTablesToDummyValues(string envName)
        {
            try
            {
                var targetDB = _databasesPersistentSource.GetDatabaseByType(envName, "Endur");

                var SQLConStr = new SqlConnectionStringBuilder
                {
                    DataSource = targetDB.ServerName,
                    IntegratedSecurity = true,
                    Pooling = false,
                    InitialCatalog = targetDB.Name,
                    TrustServerCertificate=true
                    
                };
                var SQLConnection = new SqlConnection(SQLConStr.ConnectionString);
                SQLConnection.Open();
                var strSQLList = new List<string>
                {
                    "UPDATE dbo.USER_AppConfig SET VALUE = 'Dummy Value After Restore'",
                    "UPDATE dbo.USER_EODPriceExtract SET VALUE = 'Dummy Value After Restore' where Parameter = 'ExcelFilePath'",
                    "UPDATE dbo.USER_PriceExtract SET VALUE = 'Dummy Value After Restore' where Parameter = 'ExcelFilePath'",
                    "UPDATE dbo.USER_bo_invoice_sequence SET invoice_seq = '90000' where inv_num_prefix = 'UK'",
                    "UPDATE dbo.USER_bo_invoice_sequence SET invoice_seq = '91000' where inv_num_prefix = 'SG'",
                    "UPDATE dbo.USER_bo_invoice_sequence SET invoice_seq = '92000' where inv_num_prefix = 'US'",
                    "UPDATE dbo.USER_SERVICEMONITOR SET MONITOR = 0"
                };
                foreach (var strSQL in strSQLList)
                    using (var sqlCmd = new SqlCommand(strSQL, SQLConnection))
                    {
                        sqlCmd.ExecuteNonQuery();
                    }

                SQLConnection.Close();
                return true;
            }
            catch (Exception ex)
            {
                Output($"Error occurred updating the User Tables to Dummy Values for Environment : {envName}");
                Output($"Error message:  {ex.Message}. ");
                return false;
            }
        }

        public bool UpdateEndurDBVars(string envName)
        {
            try
            {
                var targetDBName = _databasesPersistentSource.GetDatabaseByType(envName, "Endur").Name;
                var targetDBServer = _databasesPersistentSource.GetDatabaseByType(envName, "Endur").ServerName;

                var SQLConStr = new SqlConnectionStringBuilder
                {
                    DataSource = targetDBServer,
                    IntegratedSecurity = true,
                    Pooling = false,
                    InitialCatalog = targetDBName,
                    TrustServerCertificate = true
                };
                var SQLConnection = new SqlConnection(SQLConStr.ConnectionString);
                SQLConnection.Open();

                var intEnvVarID = 20161;
                var constPersonnelID = -1;
                var constUserID = 1;
                using (var sqlCmd =
                    new SqlCommand("EXEC dbo.save_global_env_config @env_id, @personnel_id, @env_value, @user_id",
                        SQLConnection))
                {
                    sqlCmd.Parameters.AddWithValue("@env_id", intEnvVarID);
                    sqlCmd.Parameters.AddWithValue("@personnel_id", constPersonnelID);
                    sqlCmd.Parameters.AddWithValue("@env_value", targetDBName);
                    sqlCmd.Parameters.AddWithValue("@user_id", constUserID);

                    sqlCmd.ExecuteNonQuery();
                }

                intEnvVarID = 20160;
                using (var sqlCmd =
                    new SqlCommand("EXEC dbo.save_global_env_config @env_id, @personnel_id, @env_value, @user_id",
                        SQLConnection))
                {
                    sqlCmd.Parameters.AddWithValue("@env_id", intEnvVarID);
                    sqlCmd.Parameters.AddWithValue("@personnel_id", constPersonnelID);
                    sqlCmd.Parameters.AddWithValue("@env_value", targetDBServer);
                    sqlCmd.Parameters.AddWithValue("@user_id", constUserID);

                    sqlCmd.ExecuteNonQuery();
                }

                return true;
            }
            catch (Exception ex)
            {
                Output(
                    $"Error occurred updating DB Name environment variable within Endur for environment : {envName}");
                Output($"Error message:  {ex.Message}. ");
                return false;
            }
        }

        public bool UpdateTPMConfig(string envName)
        {
            try
            {
                var targetDB = _databasesPersistentSource.GetDatabaseByType(envName, "Endur");

                var SQLConStr = new SqlConnectionStringBuilder
                {
                    DataSource = targetDB.ServerName,
                    IntegratedSecurity = true,
                    Pooling = false,
                    InitialCatalog = targetDB.Name,
                    TrustServerCertificate = true
                };
                var SQLConnection = new SqlConnection(SQLConStr.ConnectionString);

                SQLConnection.Open();
                using (var sqlTransaction = SQLConnection.BeginTransaction())
                {
                    var constPropID = 7003;
                    var constServiceType = 2;
                    var constServiceGroupType = 18;
                    var constPropOwnerID = 0;
                    var constOverlayOwnerID = 3780980;
                    var strPropValue = "";
                    var constUserID = 1;
                    var constExtraValueID = 20593;
                    var intStartIndex = targetDB.ServerName.IndexOf(@"\") + 1;
                    var strDBInstanceShortName = targetDB.ServerName.Substring(0, intStartIndex - 1);
                    var strSQLPort = _sqlPortsPersistentSource.GetSqlPort(targetDB.ServerName).TrimEnd();
                    strPropValue = @"jdbc:jtds:sqlserver://" + strDBInstanceShortName + @":" + strSQLPort + @"/" +
                                   targetDB.Name;
                    var strSQL =
                        "EXEC dbo.grid_property_overlay_save @prop_id, @prop_name, @service_type, @service_group_type, @prop_owner_id, @overlay_owner_id, @prop_value, @user_id, @extra_value_id";
                    Output("strPropValue [" + constPropID + "]: " + strPropValue);
                    using (var sqlCmd = new SqlCommand(strSQL, SQLConnection, sqlTransaction))
                    {
                        sqlCmd.Parameters.AddWithValue("@prop_id", constPropID);
                        sqlCmd.Parameters.AddWithValue("@prop_name", "");
                        sqlCmd.Parameters.AddWithValue("@service_type", constServiceType);
                        sqlCmd.Parameters.AddWithValue("@service_group_type", constServiceGroupType);
                        sqlCmd.Parameters.AddWithValue("@prop_owner_id", constPropOwnerID);
                        sqlCmd.Parameters.AddWithValue("@overlay_owner_id", constOverlayOwnerID);
                        sqlCmd.Parameters.AddWithValue("@prop_value", strPropValue);
                        sqlCmd.Parameters.AddWithValue("@user_id", constUserID);
                        sqlCmd.Parameters.AddWithValue("@extra_value_id", constExtraValueID);
                        sqlCmd.ExecuteNonQuery();
                        sqlTransaction.Commit();
                        sqlTransaction.Dispose();
                    }
                }

                using (var sqlTransaction2 = SQLConnection.BeginTransaction())
                {
                    var constPropID = 7037;
                    var constServiceType = 2;
                    var constServiceGroupType = 18;
                    var constPropOwnerID = 0;
                    var constOverlayOwnerID = 3780980;
                    var strPropValue = "";
                    var constUserID = 1;
                    var constExtraValueID = 20637;
                    var intStartIndex = targetDB.ServerName.IndexOf(@"\") + 1;
                    var strDBInstanceShortName = targetDB.ServerName.Substring(0, intStartIndex - 1);
                    var strSQLPort = _sqlPortsPersistentSource.GetSqlPort(targetDB.ServerName).TrimEnd();
                    strPropValue = @"jdbc:jtds:sqlserver://" + strDBInstanceShortName + @":" + strSQLPort + @"/" +
                                   targetDB.Name;
                    var strSQL =
                        "EXEC dbo.grid_property_overlay_save @prop_id, @prop_name, @service_type, @service_group_type, @prop_owner_id, @overlay_owner_id, @prop_value, @user_id, @extra_value_id";
                    Output("strPropValue [" + constPropID + "]: " + strPropValue);
                    using (var sqlCmd = new SqlCommand(strSQL, SQLConnection, sqlTransaction2))
                    {
                        sqlCmd.Parameters.AddWithValue("@prop_id", constPropID);
                        sqlCmd.Parameters.AddWithValue("@prop_name", "");
                        sqlCmd.Parameters.AddWithValue("@service_type", constServiceType);
                        sqlCmd.Parameters.AddWithValue("@service_group_type", constServiceGroupType);
                        sqlCmd.Parameters.AddWithValue("@prop_owner_id", constPropOwnerID);
                        sqlCmd.Parameters.AddWithValue("@overlay_owner_id", constOverlayOwnerID);
                        sqlCmd.Parameters.AddWithValue("@prop_value", strPropValue);
                        sqlCmd.Parameters.AddWithValue("@user_id", constUserID);
                        sqlCmd.Parameters.AddWithValue("@extra_value_id", constExtraValueID);
                        sqlCmd.ExecuteNonQuery();
                        sqlTransaction2.Commit();
                        sqlTransaction2.Dispose();
                    }
                }

                using (var sqlTransaction3 = SQLConnection.BeginTransaction())
                {
                    var constPropID = 7035;
                    var constServiceType = 2;
                    var constServiceGroupType = 18;
                    var constPropOwnerID = 0;
                    var constOverlayOwnerID = 3780980;
                    var strPropValue = "";
                    var constUserID = 1;
                    var constExtraValueID = 20635;
                    strPropValue = targetDB.ServerName;
                    var strSQL =
                        "EXEC dbo.grid_property_overlay_save @prop_id, @prop_name, @service_type, @service_group_type, @prop_owner_id, @overlay_owner_id, @prop_value, @user_id, @extra_value_id";
                    Output("strPropValue [" + constPropID + "]: " + strPropValue);
                    using (var sqlCmd = new SqlCommand(strSQL, SQLConnection, sqlTransaction3))
                    {
                        sqlCmd.Parameters.AddWithValue("@prop_id", constPropID);
                        sqlCmd.Parameters.AddWithValue("@prop_name", "");
                        sqlCmd.Parameters.AddWithValue("@service_type", constServiceType);
                        sqlCmd.Parameters.AddWithValue("@service_group_type", constServiceGroupType);
                        sqlCmd.Parameters.AddWithValue("@prop_owner_id", constPropOwnerID);
                        sqlCmd.Parameters.AddWithValue("@overlay_owner_id", constOverlayOwnerID);
                        sqlCmd.Parameters.AddWithValue("@prop_value", strPropValue);
                        sqlCmd.Parameters.AddWithValue("@user_id", constUserID);
                        sqlCmd.Parameters.AddWithValue("@extra_value_id", constExtraValueID);
                        sqlCmd.ExecuteNonQuery();
                        sqlTransaction3.Commit();
                        sqlTransaction3.Dispose();
                    }
                }

                using (var sqlTransaction4 = SQLConnection.BeginTransaction())
                {
                    var constPropID = 7036;
                    var constServiceType = 2;
                    var constServiceGroupType = 18;
                    var constPropOwnerID = 0;
                    var constOverlayOwnerID = 3780980;
                    var strPropValue = "";
                    var constUserID = 1;
                    var constExtraValueID = 20635;
                    strPropValue = targetDB.Name;
                    var strSQL =
                        "EXEC dbo.grid_property_overlay_save @prop_id, @prop_name, @service_type, @service_group_type, @prop_owner_id, @overlay_owner_id, @prop_value, @user_id, @extra_value_id";
                    Output("strPropValue [" + constPropID + "]: " + strPropValue);
                    using (var sqlCmd = new SqlCommand(strSQL, SQLConnection, sqlTransaction4))
                    {
                        sqlCmd.Parameters.AddWithValue("@prop_id", constPropID);
                        sqlCmd.Parameters.AddWithValue("@prop_name", "");
                        sqlCmd.Parameters.AddWithValue("@service_type", constServiceType);
                        sqlCmd.Parameters.AddWithValue("@service_group_type", constServiceGroupType);
                        sqlCmd.Parameters.AddWithValue("@prop_owner_id", constPropOwnerID);
                        sqlCmd.Parameters.AddWithValue("@overlay_owner_id", constOverlayOwnerID);
                        sqlCmd.Parameters.AddWithValue("@prop_value", strPropValue);
                        sqlCmd.Parameters.AddWithValue("@user_id", constUserID);
                        sqlCmd.Parameters.AddWithValue("@extra_value_id", constExtraValueID);
                        sqlCmd.ExecuteNonQuery();
                        sqlTransaction4.Commit();
                        sqlTransaction4.Dispose();
                    }
                }

                SQLConnection.Close();
            }
            catch (Exception ex)
            {
                Output($"Error occurred updating the TPM Config for Environment : {envName}");
                Output($"Error message:  {ex.Message}. ");
                return false;
            }

            return true;
        }

        public bool FoldDownRunsites(string envName, bool bolOnlyClearSchedules)
        {
            try
            {
                Output($"Folding down runsites for Environment: {envName}");
                var targetDB = _databasesPersistentSource.GetDatabaseByType(envName, "Endur");
                int appserver01RunsiteID;
                int appserver02RunsiteID;
                int appserver04RunsiteID;
                int foldedJobs;
                int ScheduleChangedJobs;

                var SQLConStr = new SqlConnectionStringBuilder
                {
                    DataSource = targetDB.ServerName,
                    IntegratedSecurity = true,
                    Pooling = false,
                    InitialCatalog = targetDB.Name,
                    TrustServerCertificate = true
                };
                var SQLConnection = new SqlConnection(SQLConStr.ConnectionString);

                SQLConnection.Open();

                var strSQL = "select id from dbo.service_mgr where service_name = 'Appserver01'";
                using (var sqlCmd = new SqlCommand(strSQL, SQLConnection))
                {
                    appserver01RunsiteID = (int)sqlCmd.ExecuteScalar();
                    Output($"Appserver01 runsite ID = {appserver01RunsiteID}");
                }

                strSQL = "select id from dbo.service_mgr where service_name = 'Appserver02'";
                using (var sqlCmd = new SqlCommand(strSQL, SQLConnection))
                {
                    appserver02RunsiteID = (int)sqlCmd.ExecuteScalar();
                    Output($"Appserver02 runsite ID = {appserver02RunsiteID}");
                }

                strSQL = "select id from dbo.service_mgr where service_name = 'Appserver04'";
                using (var sqlCmd = new SqlCommand(strSQL, SQLConnection))
                {
                    appserver04RunsiteID = (int)sqlCmd.ExecuteScalar();
                    Output($"Appserver04 runsite ID = {appserver04RunsiteID}");
                }

                strSQL =
                    "select a.job_id, a.run_site, a.service_group_type, b.service_name from dbo.job_cfg as a, dbo.service_mgr as b where a.run_site = b.id order by b.service_name";
                using (var sqlCmd = new SqlCommand(strSQL, SQLConnection))
                {
                    var endurJobSiteList = new List<EndurJobSite>();
                    using (var jobSiteReader = sqlCmd.ExecuteReader())
                    {
                        while (jobSiteReader.Read())
                        {
                            var loopJobSite = new EndurJobSite
                            {
                                jobID = jobSiteReader.GetInt32(0),
                                runSite = jobSiteReader.GetInt32(1),
                                serviceGroupType = jobSiteReader.GetInt32(2)
                            };
                            loopJobSite.serviceName = loopJobSite.jobID == 21687 ? "Appserver04" : jobSiteReader.GetString(3);
                            endurJobSiteList.Add(loopJobSite);
                        }
                    }

                    foldedJobs = 0;
                    ScheduleChangedJobs = 0;

                    foreach (var endurJobSite in endurJobSiteList)
                    {
                        if (!bolOnlyClearSchedules)
                            if (endurJobSite.serviceName != "Appserver01" && endurJobSite.serviceName != "Appserver02")
                            {
                                if (endurJobSite.serviceName.Contains("Appserver03") ||
                                    endurJobSite.serviceName.Contains("Appserver06") ||
                                    endurJobSite.serviceName.Contains("Appserver09"))
                                    endurJobSite.runSite = appserver01RunsiteID;
                                else if (endurJobSite.jobID == 21687
                                )       
                                    endurJobSite.runSite = appserver04RunsiteID;
                                else
                                    endurJobSite.runSite = appserver02RunsiteID;
                                strSQL = "EXEC dbo.change_job_cfg_runsite @job_id, @run_site, @service_group_type";
                                using (var sqlCmdUpdateRunsite = new SqlCommand(strSQL, SQLConnection))
                                {
                                    sqlCmdUpdateRunsite.Parameters.AddWithValue("@job_id", endurJobSite.jobID);
                                    sqlCmdUpdateRunsite.Parameters.AddWithValue("@run_site", endurJobSite.runSite);
                                    sqlCmdUpdateRunsite.Parameters.AddWithValue("@service_group_type",
                                        endurJobSite.serviceGroupType);
                                    var result = sqlCmdUpdateRunsite.ExecuteNonQuery();
                                }

                                foldedJobs++;
                            }   
                    }
                }

                Output("Updating workflow schedules");
                strSQL =
                    @"UPDATE dbo.job_cfg SET schedule_0 = 0, schedule_1 = 0, schedule_2 = 0, schedule_3 = 0, schedule_4 = 0, schedule_5 = 0,schedule_6 = 0, schedule_7 = 0, version = version+1,row_creation= GetDate(), entry_status = 2  WHERE type = 0 AND service_group_type = 0 and sub_id = 0";
                using (var sqlCmdUpdateSchedules = new SqlCommand(strSQL, SQLConnection))
                {
                    sqlCmdUpdateSchedules.ExecuteNonQuery();
                }

                if (!bolOnlyClearSchedules)
                {
                    Output("Updating Reval Services schedules to manual");
                    strSQL = "UPDATE dbo.job_cfg SET schedule_0 = 0,version = version+1,row_creation= GetDate(), entry_status = 2  where type = 0  and sub_id = 0 and service_group_type =12";
                    using (var sqlCmdUpdateSchedules = new SqlCommand(strSQL, SQLConnection))
                    {
                        sqlCmdUpdateSchedules.ExecuteNonQuery();
                    }

                    Output("Updating Maintenance Services schedules to manual");
                    strSQL = "UPDATE dbo.job_cfg SET schedule_0 = 0,version = version+1,row_creation= GetDate(), entry_status = 2  where type=0  and sub_id=0 and service_group_type =36";
                    using (var sqlCmdUpdateSchedules = new SqlCommand(strSQL, SQLConnection))
                    {
                        sqlCmdUpdateSchedules.ExecuteNonQuery();
                    }
                }

                Output($"{foldedJobs} Jobs have been folded down for Environment : {envName}");
                Output($"{ScheduleChangedJobs} Jobs have been changed to manual for Environment : {envName}");
                Output($"Truncating the JOB_RUNNING table for Environment : {envName}");
                strSQL = "TRUNCATE TABLE dbo.JOB_RUNNING";
                using (var sqlCmdTruncate = new SqlCommand(strSQL, SQLConnection))
                {
                    sqlCmdTruncate.ExecuteNonQuery();
                }

                Output("Updating the eCMS config to make sure eCMS is run as user appserv02");
                strSQL = "update dbo.grid_property_overlays set prop_value = 'appserv02' where prop_id = 43";
                using (var sqlCmdUpdateGrid = new SqlCommand(strSQL, SQLConnection))
                {
                    sqlCmdUpdateGrid.ExecuteNonQuery();
                }

                SQLConnection.Close();
                return true;
            }
            catch (Exception ex)
            {
                Output($"Error occurred folding down the runsites for Environment : {envName}");
                Output($"Error message:  {ex.Message}. ");
                return false;
            }
        }

        private EndurJobCfg PopulateEndurJobCfg(int jobID, SqlConnection sqlConn)
        {
            try
            {
                var jobCfg = new EndurJobCfg();
                var strSQL =
                    "SELECT job_id, wflow_id, type, sub_id, name, sequence, personnel_type, run_site, max_run, exc_type, exc_id, what_next, owner, scope, schedule_0, schedule_1, schedule_2, schedule_3, schedule_4, schedule_5, schedule_6, schedule_7, subscription, wflow_sequence, pin_flag, service_type, service_group_type, job_run_site, grid_scheduler, rerun_counter, script_log, user_id,wflow_mgr_cat_id FROM dbo.job_cfg WHERE JOB_ID = " +
                    jobID;
                using (var sqlCmd = new SqlCommand(strSQL, sqlConn))
                {
                    using (var jobCfgReader = sqlCmd.ExecuteReader())
                    {
                        jobCfgReader.Read();
                        jobCfg.job_id = jobCfgReader.GetInt32(0);
                        jobCfg.wflow_id = jobCfgReader.GetInt32(1);
                        jobCfg.type = jobCfgReader.GetInt32(2);
                        jobCfg.sub_id = jobCfgReader.GetInt32(3);
                        jobCfg.name = jobCfgReader.GetString(4);
                        jobCfg.sequence = jobCfgReader.GetInt32(5);
                        jobCfg.personnel_type = jobCfgReader.GetInt32(6);
                        jobCfg.run_site = jobCfgReader.GetInt32(7);
                        jobCfg.max_run = jobCfgReader.GetInt32(8);
                        jobCfg.exc_type = jobCfgReader.GetInt32(9);
                        jobCfg.exc_id = jobCfgReader.GetInt32(10);
                        jobCfg.what_next = jobCfgReader.GetInt32(11);
                        jobCfg.owner = jobCfgReader.GetInt32(12);
                        jobCfg.scope = jobCfgReader.GetInt32(13);
                        jobCfg.schedule_0 = jobCfgReader.GetInt32(14);
                        jobCfg.schedule_1 = jobCfgReader.GetInt32(15);
                        jobCfg.schedule_2 = jobCfgReader.GetInt32(16);
                        jobCfg.schedule_3 = jobCfgReader.GetInt32(17);
                        jobCfg.schedule_4 = jobCfgReader.GetInt32(18);
                        jobCfg.schedule_5 = jobCfgReader.GetInt32(19);
                        jobCfg.schedule_6 = jobCfgReader.GetInt32(20);
                        jobCfg.schedule_7 = jobCfgReader.GetInt32(21);
                        jobCfg.subscription = jobCfgReader.GetString(22);
                        jobCfg.wflow_sequence = jobCfgReader.GetInt32(23);
                        jobCfg.pin_flag = jobCfgReader.GetByte(24);
                        jobCfg.service_type = jobCfgReader.GetInt32(25);
                        jobCfg.service_group_type = jobCfgReader.GetInt32(26);
                        jobCfg.job_run_site = jobCfgReader.GetInt32(27);
                        jobCfg.grid_scheduler = jobCfgReader.GetInt32(28);
                        jobCfg.rerun_counter = jobCfgReader.GetInt32(29);
                        jobCfg.script_log = jobCfgReader.GetInt32(30);
                        jobCfg.user_id = jobCfgReader.GetInt32(31);
                        jobCfg.wflow_mgr_cat_id = jobCfgReader.GetInt32(32);

                        if (jobCfg.schedule_0 != 8)
                        {
                            jobCfg.schedule_0 = 0;
                        }
                        jobCfg.schedule_1 = 0;
                        jobCfg.schedule_2 = 0;
                        jobCfg.schedule_3 = 0;
                        jobCfg.schedule_4 = 0;
                        if (jobCfg.schedule_0 != 8)
                        {
                            jobCfg.schedule_5 = 0;
                        }
                        jobCfg.schedule_6 = 0;
                        jobCfg.schedule_7 = 0;
                    }

                    sqlCmd.Dispose();
                }

                return jobCfg;
            }
            catch (Exception ex)
            {
                Output($"Error occurred getting Job Config information for Job ID : {jobID}");
                Output($"Error message:  {ex.Message}. ");
                return null;
            }
        }

        public bool UpdateEndurUsers(string envName)
        {
            Output("Resetting user password date for: " + envName);
            try
            {
                var targetDB = _databasesPersistentSource.GetDatabaseByType(envName, "Endur");

                var SQLConStr = new SqlConnectionStringBuilder
                {
                    DataSource = targetDB.ServerName,
                    IntegratedSecurity = true,
                    Pooling = false,
                    InitialCatalog = targetDB.Name,
                    TrustServerCertificate = true
                };
                var SQLConnection = new SqlConnection(SQLConStr.ConnectionString);
                SQLConnection.Open();

                UpdatePasswordDates(SQLConnection);
                SQLConnection.Close();
                return true;
            }
            catch (Exception ex)
            {
                Output("Error occurred re-associating Endur users");
                Output($"Error message:  {ex.Message}. ");
                return false;
            }
        }

        public bool UpdatePasswordDates(SqlConnection sqlConn)
        {
            try
            {
                var currentDate = DateTime.Now.Date;
                var strSQL =
                    "update dbo.[personnel] set password_date = @currentDate where password_never_expires <> 1";
                using (var sqlCmd = new SqlCommand(strSQL, sqlConn))
                {
                    sqlCmd.Parameters.AddWithValue("@currentDate", currentDate);
                    sqlCmd.ExecuteNonQuery();
                    sqlCmd.Dispose();
                }

                return true;
            }
            catch (Exception ex)
            {
                Output("Error occurred updating Password Expiry Dates within Endur");
                Output($"Error message:  {ex.Message}. ");
                return false;
            }
        }

        private void Output(string strText)
        {
            Console.WriteLine(DateTime.Now + " - " + strText);
            _logger?.LogInformation(strText);
        }
    }
}