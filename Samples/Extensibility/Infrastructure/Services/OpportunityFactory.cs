﻿// Copyright(c) Microsoft Corporation. 
// All rights reserved.
//
// Licensed under the MIT license. See LICENSE file in the solution root folder for full license information

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ApplicationCore.Artifacts;
using ApplicationCore.Interfaces;
using ApplicationCore.Entities;
using ApplicationCore.Services;
using ApplicationCore.Authorization;
using ApplicationCore;
using ApplicationCore.Helpers;
using ApplicationCore.Entities.GraphServices;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Infrastructure.GraphApi;
using ApplicationCore.Helpers.Exceptions;
using Microsoft.Graph;
using System.Linq;
using System.Text.RegularExpressions;
using Infrastructure.DealTypeServices;
using ApplicationCore.Models;
using Infrastructure.Authorization;

namespace Infrastructure.Services
{
	public class OpportunityFactory : BaseArtifactFactory<Opportunity>, IOpportunityFactory
	{
		private readonly GraphSharePointAppService _graphSharePointAppService;
		private readonly GraphUserAppService _graphUserAppService;
		private readonly IUserProfileRepository _userProfileRepository;
        private readonly IRoleMappingRepository _roleMappingRepository;
        private readonly CardNotificationService _cardNotificationService;
        private readonly IUserContext _userContext;
        private readonly CheckListProcessService _checkListProcessService;
        private readonly CustomerFeedbackProcessService customerFeedbackProcessService;
        private readonly CustomerDecisionProcessService _customerDecisionProcessService;
        private readonly ProposalStatusProcessService _proposalStatusProcessService;
        private readonly NewOpportunityProcessService _newOpportunityProcessService;
        private readonly StartProcessService _startProcessService;
        private readonly IDashboardService _dashboardService;
        private readonly IAuthorizationService _authorizationService;
        private readonly IPermissionRepository _permissionRepository;
        private readonly IDashboardAnalysis _dashboardAnalysis;
        protected readonly GraphTeamsAppService _graphTeamsAppService;
        private readonly IAddInHelper _addInHelper;


        public OpportunityFactory(
            ILogger<OpportunityFactory> logger,
            IOptionsMonitor<AppOptions> appOptions,
            GraphSharePointAppService graphSharePointAppService,
            GraphUserAppService graphUserAppService,
            IUserProfileRepository userProfileRepository,
            IRoleMappingRepository roleMappingRepository,
            CardNotificationService cardNotificationService,
            IUserContext userContext,
            CheckListProcessService checkListProcessService,
            CustomerFeedbackProcessService customerFeedbackProcessService,
            CustomerDecisionProcessService customerDecisionProcessService,
            ProposalStatusProcessService proposalStatusProcessService,
            NewOpportunityProcessService newOpportunityProcessService,
            IDashboardService dashboardService,
            IAuthorizationService authorizationService,
            IPermissionRepository permissionRepository,
            StartProcessService startProcessService,
            IDashboardAnalysis dashboardAnalysis,
            GraphTeamsAppService graphTeamsAppService,
            IAddInHelper addInHelper) : base(logger, appOptions)
		{
			Guard.Against.Null(graphSharePointAppService, nameof(graphSharePointAppService));
			Guard.Against.Null(graphUserAppService, nameof(graphUserAppService));
			Guard.Against.Null(userProfileRepository, nameof(userProfileRepository));
            Guard.Against.Null(roleMappingRepository, nameof(roleMappingRepository));
            Guard.Against.Null(cardNotificationService, nameof(cardNotificationService));
            Guard.Against.Null(userContext, nameof(userContext));
            Guard.Against.Null(checkListProcessService, nameof(checkListProcessService));
            Guard.Against.Null(customerDecisionProcessService, nameof(customerDecisionProcessService));
            Guard.Against.Null(proposalStatusProcessService, nameof(proposalStatusProcessService));
            Guard.Against.Null(newOpportunityProcessService, nameof(newOpportunityProcessService));
            Guard.Against.Null(startProcessService, nameof(startProcessService));
            Guard.Against.Null(dashboardService, nameof(dashboardService));
            Guard.Against.Null(dashboardAnalysis, nameof(dashboardAnalysis));
            Guard.Against.Null(authorizationService, nameof(authorizationService));
            Guard.Against.Null(permissionRepository, nameof(permissionRepository));
            Guard.Against.Null(graphTeamsAppService, nameof(graphTeamsAppService));
            Guard.Against.Null(addInHelper, nameof(addInHelper));

            _graphSharePointAppService = graphSharePointAppService;
			_graphUserAppService = graphUserAppService;
            _userProfileRepository = userProfileRepository;
            _roleMappingRepository = roleMappingRepository;
            _cardNotificationService = cardNotificationService;
            _userContext = userContext;
            _checkListProcessService = checkListProcessService;
            this.customerFeedbackProcessService = customerFeedbackProcessService;
            _customerDecisionProcessService = customerDecisionProcessService;
            _proposalStatusProcessService = proposalStatusProcessService;
            _newOpportunityProcessService = newOpportunityProcessService;
            _startProcessService = startProcessService;
            _dashboardService = dashboardService;
            _authorizationService = authorizationService;
            _permissionRepository = permissionRepository;
            _graphTeamsAppService = graphTeamsAppService;
            _dashboardAnalysis = dashboardAnalysis;
            _addInHelper = addInHelper;
        }

        public async Task<Opportunity> CreateWorkflowAsync(Opportunity opportunity, string requestId = "")
		{
			try
			{
				// Set initial opportunity state
				opportunity.Metadata.OpportunityState = OpportunityState.Creating;

				// Remove empty sections from proposal document
				var porposalSectionList = new List<DocumentSection>();
				foreach (var item in opportunity.Content.ProposalDocument.Content.ProposalSectionList)
				{
					if (!String.IsNullOrEmpty(item.DisplayName))
					{
						porposalSectionList.Add(item);
					}
				}
				opportunity.Content.ProposalDocument.Content.ProposalSectionList = porposalSectionList;


                // Delete empty ChecklistItems
                opportunity.Content.Checklists = await RemoveEmptyFromChecklistAsync(opportunity.Content.Checklists, requestId);

                //Granular Access : Start
                _logger.LogError($"RequestId: {requestId} - Opportunityfactory_UpdateItemAsync CheckAccess CreateItemAsync");
                if (opportunity.Content.DealType.ProcessList != null)
                {
                    //create team and channels
                    if (await GroupIdCheckAsync(opportunity.DisplayName, requestId))
                        await CreateTeamAndChannelsAsync(opportunity, requestId);

                    if (StatusCodes.Status200OK == await _authorizationService.CheckAccessFactoryAsync(PermissionNeededTo.DealTypeWrite, requestId) ||
                        await _authorizationService.CheckAccessInOpportunityAsync(opportunity, PermissionNeededTo.Write, requestId))
                    {
                        bool checklistPass = false;
                        foreach (var item in opportunity.Content.DealType.ProcessList)
                        {
                            if (item.ProcessType.ToLower() == "checklisttab" && checklistPass == false)
                            {
                                //DashBoard Create call Start.
                                await UpdateDashBoardEntryAsync(opportunity, requestId);
                                //DashBoard Create call End.
                                opportunity = await _checkListProcessService.CreateWorkflowAsync(opportunity, requestId);
                                checklistPass = true;
                            }
                            else if (item.ProcessType.ToLower() == "feedbacktab")
                            {
                                opportunity = await customerFeedbackProcessService.CreateWorkflowAsync(opportunity, requestId);
                            }
                            else if (item.ProcessType.ToLower() == "customerdecisiontab")
                            {
                                opportunity = await _customerDecisionProcessService.CreateWorkflowAsync(opportunity, requestId);
                            }
                            else if (item.ProcessType.ToLower() == "proposalstatustab")
                            {
                                opportunity = await _proposalStatusProcessService.CreateWorkflowAsync(opportunity, requestId);
                            }
                            else if (item.ProcessStep.ToLower() == "start process")
                            {
                                opportunity = await _startProcessService.CreateWorkflowAsync(opportunity, requestId);
                            }
                            else if (item.ProcessStep.ToLower() == "new opportunity")
                            {
                                opportunity = await _newOpportunityProcessService.CreateWorkflowAsync(opportunity, requestId);
                            }
                        }
                    }
                    else
                    {
                        if (opportunity.Content.DealType != null)
                        {
                            _logger.LogError($"RequestId: {requestId} - CreateWorkflowAsync Service Exception");
                            throw new AccessDeniedException($"RequestId: {requestId} - CreateWorkflowAsync Service Exception");
                        }
                    }
                }


                // Update note created by (if one) and set it to relationship manager
                if (opportunity.Content.Notes != null)
				{
					if (opportunity.Content.Notes?.Count > 0)
					{
						var currentUser = (_userContext.User.Claims).ToList().Find(x => x.Type == "preferred_username")?.Value;
						var callerUser = await _userProfileRepository.GetItemByUpnAsync(currentUser, requestId);

						if (callerUser != null)
						{
							opportunity.Content.Notes[0].CreatedBy = callerUser;
							opportunity.Content.Notes[0].CreatedDateTime = DateTimeOffset.Now;

						}
						else
						{
							_logger.LogWarning($"RequestId: {requestId} - CreateWorkflowAsync can't find {currentUser} to set note created by");
						}
					}
				}

                // Send notification
                // Define Sent To user profile
                var loanOfficer = opportunity.Content.TeamMembers.ToList().Find(x => x.AssignedRole.DisplayName == "LoanOfficer");             

                if (loanOfficer != null)
                {
                    try
                    {
                        _logger.LogInformation($"RequestId: {requestId} - CreateWorkflowAsync sendNotificationCardAsync new opportunity notification.");
                        var sendAccount = UserProfile.Empty;
                        sendAccount.Id = loanOfficer.Id;
                        sendAccount.DisplayName = loanOfficer.DisplayName;
                        sendAccount.Fields.UserPrincipalName = loanOfficer.Fields.UserPrincipalName;
                        var sendNotificationCard = await _cardNotificationService.sendNotificationCardAsync(opportunity, sendAccount, $"New opportunity {opportunity.DisplayName} has been assigned to ", requestId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"RequestId: {requestId} - CreateWorkflowAsync sendNotificationCardAsync Action error: {ex}");
                    }
                }

                //Adding RelationShipManager and LoanOfficer into ProposalManager Team
                foreach (var item in opportunity.Content.TeamMembers)
                {
                    try
                    {
                        if (item.AssignedRole.DisplayName == "LoanOfficer" || item.AssignedRole.DisplayName == "RelationshipManager")
                        {
                            dynamic jsonDyn = null;
                            var options = new List<QueryParam>();
                            options.Add(new QueryParam("filter", $"startswith(displayName,'"+_appOptions.GeneralProposalManagementTeam+"')"));
                            var groupIdJson = await _graphUserAppService.GetGroupAsync(options, "", requestId);
                            jsonDyn = groupIdJson;
                            if (!String.IsNullOrEmpty(jsonDyn.value[0].id.ToString()))
                            {
                                var groupID = jsonDyn.value[0].id.ToString();
                                if (!String.IsNullOrEmpty(item.Fields.UserPrincipalName))
                                {
                                    try
                                    {
                                        Guard.Against.NullOrEmpty(item.Id, $"OpportunityFactorty_{item.AssignedRole.DisplayName} Id NullOrEmpty", requestId);
                                        var responseJson = await _graphUserAppService.AddGroupMemberAsync(item.Id, groupID, requestId);
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError($"RequestId: {requestId} - userId: {item.Id} - OpportunityFactorty_AddGroupMemberAsync_{item.AssignedRole.DisplayName} error in CreateWorkflowAsync: {ex}");
                                    }
                                }

                            }
                        }
                    }
                    catch(Exception ex)
                    {
                        _logger.LogError($"RequestId: {requestId} - userId: {item.Id} - OpportunityFactorty_AddGroupMemberAsync_{item.AssignedRole.DisplayName} error in CreateWorkflowAsync: {ex}");
                    }
                }
                //Adding RelationShipManager and LoanOfficer into ProposalManager Team
                return opportunity;
			}
			catch (Exception ex)
			{
				_logger.LogError($"RequestId: {requestId} - CreateWorkflowAsync Service Exception: {ex}");
				throw new ResponseException($"RequestId: {requestId} - CreateWorkflowAsync Service Exception: {ex}");
			}
		}

        public async Task<Opportunity> UpdateWorkflowAsync(Opportunity opportunity, string requestId = "")
		{

            try
			{
                //create team and channels
                if (opportunity.Content.DealType.ProcessList != null && opportunity.Metadata.OpportunityState == OpportunityState.Creating)
                {
                    if (await GroupIdCheckAsync(opportunity.DisplayName, requestId))
                    {
                        await CreateTeamAndChannelsAsync(opportunity, requestId);
                        //Temperary change, will revert to back after implementing "add app"
                        opportunity.Metadata.OpportunityState = OpportunityState.InProgress;
                    }
                }

                var initialState = opportunity.Metadata.OpportunityState;
                bool checklistPass = false;

                if (opportunity.Metadata.OpportunityState != OpportunityState.Creating)
                {

                    try
                    {
                        opportunity = await MoveTempFileToTeamAsync(opportunity, requestId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"RequestId: {requestId} - UpdateWorkflowAsync_MoveTempFileToTeam Service Exception: {ex}");
                    }

                    if (opportunity.Content.DealType.ProcessList != null)
                    {

                        foreach (var item in opportunity.Content.DealType.ProcessList)
                        {
                            if (item.ProcessType.ToLower() == "checklisttab" && checklistPass == false)
                            {
                                //DashBoard Create call Start.
                                await UpdateDashBoardEntryAsync(opportunity, requestId);
                                //DashBoard Create call End.
                                opportunity = await _checkListProcessService.UpdateWorkflowAsync(opportunity, requestId);
                                checklistPass = true;
                            }
                            else if (item.ProcessType.ToLower() == "feedbacktab")
                            {
                                opportunity = await customerFeedbackProcessService.UpdateWorkflowAsync(opportunity, requestId);
                            }
                            else if (item.ProcessType.ToLower() == "customerdecisiontab")
                            {
                                opportunity = await _customerDecisionProcessService.UpdateWorkflowAsync(opportunity, requestId);
                            }
                            else if (item.ProcessType.ToLower() == "proposalstatustab")
                            {
                                opportunity = await _proposalStatusProcessService.UpdateWorkflowAsync(opportunity, requestId);
                            }
                            else if (item.ProcessStep.ToLower() == "start process")
                            {
                                opportunity = await _startProcessService.UpdateWorkflowAsync(opportunity, requestId);
                            }
                            else if (item.ProcessStep.ToLower() == "new opportunity")
                            {
                                opportunity = await _newOpportunityProcessService.UpdateWorkflowAsync(opportunity, requestId);
                            }
                        }
                    }


                    var roleMappings = (await _roleMappingRepository.GetAllAsync(requestId)).ToList();

                }

                // Send notification
                _logger.LogInformation($"RequestId: {requestId} - UpdateWorkflowAsync initialState: {initialState.Name} - {opportunity.Metadata.OpportunityState.Name}");
                if (initialState.Value != opportunity.Metadata.OpportunityState.Value)
                {
                    try
                    {
                        _logger.LogInformation($"RequestId: {requestId} - CreateWorkflowAsync sendNotificationCardAsync opportunity state change notification.");
                        var sendTo = UserProfile.Empty;
                        var sendNotificationCard = await _cardNotificationService.sendNotificationCardAsync(opportunity, sendTo, $"Opportunity state for {opportunity.DisplayName} has been changed to {opportunity.Metadata.OpportunityState.Name}", requestId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"RequestId: {requestId} - CreateWorkflowAsync sendNotificationCardAsync OpportunityState error: {ex}");
                    }
                }

                //Adding RelationShipManager and LoanOfficer into ProposalManager Team
                foreach (var item in opportunity.Content.TeamMembers)
                {
                    try
                    {
                        if (item.AssignedRole.DisplayName == "LoanOfficer" || item.AssignedRole.DisplayName == "RelationshipManager")
                        {
                            dynamic jsonDyn = null;
                            var options = new List<QueryParam>();
                            options.Add(new QueryParam("filter", $"startswith(displayName,'Proposal Manager Team')"));
                            var groupIdJson = await _graphUserAppService.GetGroupAsync(options, "", requestId);
                            jsonDyn = groupIdJson;
                            if (!String.IsNullOrEmpty(jsonDyn.value[0].id.ToString()))
                            {
                                var groupID = jsonDyn.value[0].id.ToString();
                                if (!String.IsNullOrEmpty(item.Fields.UserPrincipalName))
                                {
                                    try
                                    {
                                        Guard.Against.NullOrEmpty(item.Id, $"OpportunityFactorty_{item.AssignedRole.DisplayName} Id NullOrEmpty", requestId);
                                        var responseJson = await _graphUserAppService.AddGroupMemberAsync(item.Id, groupID, requestId);
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError($"RequestId: {requestId} - userId: {item.Id} - OpportunityFactorty_AddGroupMemberAsync_{item.AssignedRole.DisplayName} error in CreateWorkflowAsync: {ex}");
                                    }
                                }

                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"RequestId: {requestId} - userId: {item.Id} - OpportunityFactorty_AddGroupMemberAsync_{item.AssignedRole.DisplayName} error in CreateWorkflowAsync: {ex}");
                    }
                }
                //Adding RelationShipManager and LoanOfficer into ProposalManager Team

                return opportunity;
			}
			catch (Exception ex)
			{
				_logger.LogError($"RequestId: {requestId} - UpdateWorkflowAsync Service Exception: {ex}");
				throw new ResponseException($"RequestId: {requestId} - UpdateWorkflowAsync Service Exception: {ex}");
			}
		}

        

        // Workflow Actions
        public async Task<Opportunity> MoveTempFileToTeamAsync(Opportunity opportunity, string requestId = "")
		{
			try
			{

				// Find entries that need to be moved
				var moveFiles = false;
				foreach(var itm in opportunity.DocumentAttachments)
				{
					if (itm.DocumentUri == "TempFolder") moveFiles = true;
				}

				if (moveFiles)
				{
					var fromSiteId = _appOptions.ProposalManagementRootSiteId;
					var toSiteId = String.Empty;
					var fromItemPath = String.Empty;
					var toItemPath = String.Empty;

					string pattern = @"[ `~!@#$%^&*()_|+\-=?;:'" + '"' + @",.<>\{\}\[\]\\\/]";
					string replacement = "";

					Regex regEx = new Regex(pattern);
					var path = regEx.Replace(opportunity.DisplayName, replacement);
					//var path = WebUtility.UrlEncode(opportunity.DisplayName);
					//var path = opportunity.DisplayName.Replace(" ", "");

					var siteIdResponse = await _graphSharePointAppService.GetSiteIdAsync(_appOptions.SharePointHostName, path, requestId);
					dynamic responseDyn = siteIdResponse;
					toSiteId = responseDyn.id.ToString();

					if (!String.IsNullOrEmpty(toSiteId))
					{
						var updatedDocumentAttachments = new List<DocumentAttachment>();
						foreach (var itm in opportunity.DocumentAttachments)
						{
							var updDoc = DocumentAttachment.Empty;
							if (itm.DocumentUri == "TempFolder")
							{
								fromItemPath = $"TempFolder/{opportunity.DisplayName}/{itm.FileName}";
								toItemPath = $"General/{itm.FileName}";

								var resp = new JObject();
								try
								{
									resp = await _graphSharePointAppService.MoveFileAsync(fromSiteId, fromItemPath, toSiteId, toItemPath, requestId);
									updDoc.Id = new Guid().ToString();
									updDoc.DocumentUri = String.Empty;
									//doc.Id = resp.id;
								}
								catch (Exception ex)
								{
									_logger.LogWarning($"RequestId: {requestId} - MoveTempFileToTeam: from: {fromItemPath} to: {toItemPath} Service Exception: {ex}");
								}
							}

							updDoc.FileName = itm.FileName;
							updDoc.Note = itm.Note ?? String.Empty;
							updDoc.Tags = itm.Tags ?? String.Empty;
							updDoc.Category = Category.Empty;
							updDoc.Category.Id = itm.Category.Id;
							updDoc.Category.Name = itm.Category.Name;

							updatedDocumentAttachments.Add(updDoc);
						}

						opportunity.DocumentAttachments = updatedDocumentAttachments;

						// Delete temp files
						var result = await _graphSharePointAppService.DeleteFileOrFolderAsync(_appOptions.ProposalManagementRootSiteId, $"TempFolder/{opportunity.DisplayName}", requestId);

					}
				}

				return opportunity;
			}
			catch (Exception ex)
			{
				_logger.LogError($"RequestId: {requestId} - MoveTempFileToTeam Service Exception: {ex}");
				throw new ResponseException($"RequestId: {requestId} - MoveTempFileToTeam Service Exception: {ex}");
			}
		}

        public Task<IList<Checklist>> RemoveEmptyFromChecklistAsync(IList<Checklist> checklists, string requestId = "")
		{
			try
			{
				var newChecklists = new List<Checklist>();
				foreach (var item in checklists)
				{
					var newChecklist = new Checklist();
					newChecklist.ChecklistTaskList = new List<ChecklistTask>();
					newChecklist.ChecklistChannel = item.ChecklistChannel;
					newChecklist.ChecklistStatus = item.ChecklistStatus;
					newChecklist.Id = item.Id;

					
					foreach (var sItem in item.ChecklistTaskList)
					{
						var newChecklistTask = new ChecklistTask();
						if (!String.IsNullOrEmpty(sItem.Id) && !String.IsNullOrEmpty(sItem.ChecklistItem))
						{
							newChecklistTask.Id = sItem.Id;
							newChecklistTask.ChecklistItem = sItem.ChecklistItem;
							newChecklistTask.Completed = sItem.Completed;
							newChecklistTask.FileUri = sItem.FileUri;

							newChecklist.ChecklistTaskList.Add(newChecklistTask);
						}
					}

					newChecklists.Add(newChecklist);
				}

				return Task.FromResult<IList<Checklist>>(newChecklists);
			}
			catch (Exception ex)
			{
				_logger.LogError($"RequestId: {requestId} - RemoveEmptyFromChecklist Service Exception: {ex}");
				throw new ResponseException($"RequestId: {requestId} - RemoveEmptyFromChecklist Service Exception: {ex}");
			}
		}

        private async Task UpdateDashBoardEntryAsync(Opportunity opportunity, string requestId)
        {
            _logger.LogInformation($"RequestId: {requestId} - UpdateDashBoardEntryAsync called.");
            try
            {
                var dashboardmodel = await _dashboardService.GetAsync(opportunity.Id, requestId);
                if (dashboardmodel != null)
                {
                    var date = DateTimeOffset.Now.Date;

                    dashboardmodel.LoanOfficer = opportunity.Content.TeamMembers.ToList().Find(x => x.AssignedRole.DisplayName == "LoanOfficer").DisplayName ?? "";
                    dashboardmodel.RelationshipManager = opportunity.Content.TeamMembers.ToList().Find(x => x.AssignedRole.DisplayName == "RelationshipManager").DisplayName ?? "";

                    if (dashboardmodel.Status.ToLower() != opportunity.Metadata.OpportunityState.Name.ToLower())
                    {
                        dashboardmodel.Status = opportunity.Metadata.OpportunityState.Name.ToString();
                        dashboardmodel.StatusChangedDate = date;
                        if (dashboardmodel.Status.ToLower().ToString() == "accepted" ||
                            dashboardmodel.Status.ToLower().ToString() == "archived")
                        {
                            dashboardmodel.OpportunityEndDate = date;
                            //first logic change from sharepoint
                            dashboardmodel.TotalNoOfDays = _dashboardAnalysis.GetDateDifference(dashboardmodel.StartDate, date, dashboardmodel.StartDate);
                        }

                    }

                    var oppCheckLists = opportunity.Content.Checklists.ToList();
                    var updatedDealTypeList = new List<Process>();

                    foreach (var process in opportunity.Content.DealType.ProcessList)
                    {
                        if (process.ProcessType.ToLower() == "checklisttab")
                        {
                            var checklistItm = oppCheckLists.Find(x => x.ChecklistChannel.ToLower() == process.Channel.ToLower());
                            //TODO: CHeck checklist is not null
                            if (checklistItm != null)
                            {
                                if (process.Status != checklistItm.ChecklistStatus)
                                {
                                    switch (checklistItm.ChecklistChannel.ToLower())
                                    {
                                        case "risk assessment":
                                            if (checklistItm.ChecklistStatus == ActionStatus.Completed)
                                            {
                                                dashboardmodel.RiskAssesmentCompletionDate = date;
                                                dashboardmodel.RiskAssessmentNoOfDays = _dashboardAnalysis.GetDateDifference(
                                                    dashboardmodel.RiskAssesmentStartDate, date, dashboardmodel.StartDate);
                                            }
                                            else if (checklistItm.ChecklistStatus == ActionStatus.InProgress)
                                                dashboardmodel.RiskAssesmentStartDate = date;
                                            break;
                                        case "credit check":
                                            if (checklistItm.ChecklistStatus == ActionStatus.Completed)
                                            {
                                                dashboardmodel.CreditCheckCompletionDate = date;
                                                dashboardmodel.CreditCheckNoOfDays = _dashboardAnalysis.GetDateDifference(
                                                    dashboardmodel.CreditCheckStartDate, date, dashboardmodel.StartDate);
                                            }
                                            else if (checklistItm.ChecklistStatus == ActionStatus.InProgress)
                                                dashboardmodel.CreditCheckStartDate = date;
                                            break;
                                        case "compliance":
                                            if (checklistItm.ChecklistStatus == ActionStatus.Completed)
                                            {
                                                dashboardmodel.ComplianceReviewComplteionDate = date;
                                                dashboardmodel.ComplianceReviewNoOfDays = _dashboardAnalysis.GetDateDifference(
                                                    dashboardmodel.ComplianceReviewStartDate, date, dashboardmodel.StartDate);
                                            }
                                            else if (checklistItm.ChecklistStatus == ActionStatus.InProgress)
                                                dashboardmodel.ComplianceReviewStartDate = date;
                                            break;
                                        case "formal proposal":
                                            if (checklistItm.ChecklistStatus == ActionStatus.Completed)
                                            {
                                                dashboardmodel.FormalProposalCompletionDate = date;
                                                dashboardmodel.FormalProposalNoOfDays = _dashboardAnalysis.GetDateDifference(
                                                    dashboardmodel.FormalProposalStartDate, date, dashboardmodel.StartDate);
                                            }
                                            else if (checklistItm.ChecklistStatus == ActionStatus.InProgress)
                                                dashboardmodel.FormalProposalStartDate = date;
                                            break;
                                    }
                                }
                            }

                        }
                    }

                    var result = await _dashboardService.UpdateOpportunityAsync(dashboardmodel, requestId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"RequestId: {requestId} - UpdateDashBoardEntryAsync Service Exception: {ex}");
            }
        }

        private async Task CreateTeamAndChannelsAsync(Opportunity opportunity, string requestId = "")
        {
            _logger.LogError($"RequestId: {requestId} - createTeamAndChannels Started");

            try
            {
                //create team
               await _graphTeamsAppService.CreateTeamAsync(opportunity.DisplayName, requestId);

                //get Group ID
                bool check = true;
                dynamic jsonDyn = null;
                var opportunityName = WebUtility.UrlEncode(opportunity.DisplayName);
                var options = new List<QueryParam>();
                options.Add(new QueryParam("filter", $"startswith(displayName,'{opportunityName}')"));
                while (check)
                {
                    var groupIdJson = await _graphUserAppService.GetGroupAsync(options, "", requestId);
                    jsonDyn = groupIdJson;
                    JArray jsonArray = JArray.Parse(jsonDyn["value"].ToString());
                    if (jsonArray.Count() > 0)
                    {
                        if (!String.IsNullOrEmpty(jsonDyn.value[0].id.ToString()))
                            check = false;
                    }

                }
                var groupID = String.Empty;
                groupID = jsonDyn.value[0].id.ToString();

                foreach (var process in opportunity.Content.DealType.ProcessList)
                {
                    if (process.Channel.ToLower() != "none")
                       await _graphTeamsAppService.CreateChannelAsync(groupID, process.Channel, process.Channel + " Channel");
                }

                //adding app is currently not supported this code is there so that we can add the app to the team once
                //graph api supports usercontext for this functionality
                try
                {
                    await _graphTeamsAppService.AddAppToTeamAsync(groupID);
                }
                catch(Exception ex)
                {
                    _logger.LogError($"RequestId: {requestId} - CreateTeamAndChannels_AddAppToTeamAsync Service Exception: {ex}");
                }
                
                try
                {
                    // Call to AddIn helper once team has been created
                    var resCallAddIn = await _addInHelper.CallAddInWebhookAsync(opportunity, requestId);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"RequestId: {requestId} -_addInHelper.CallAddInWebhookAsync(opportunity, requestId): {ex}");
                }
            }
            catch(Exception ex)
            {
                _logger.LogError($"RequestId: {requestId} - CreateTeamAndChannels Service Exception: {ex}");
                throw new ResponseException($"RequestId: {requestId} - CreateTeamAndChannels Service Exception: {ex}");
            }
        }

        private async Task<bool> GroupIdCheckAsync(string displayName, string requestId = "")
        {
            bool check = true;
            dynamic jsonDyn = null;
            var opportunityName = WebUtility.UrlEncode(displayName);
            var options = new List<QueryParam>();
            options.Add(new QueryParam("filter", $"startswith(displayName,'{opportunityName}')"));
            var groupIdJson = await _graphUserAppService.GetGroupAsync(options, "", requestId);
            jsonDyn = groupIdJson;
            JArray jsonArray = JArray.Parse(jsonDyn["value"].ToString());
            if (jsonArray.Count() > 0)
            {
                if (!String.IsNullOrEmpty(jsonDyn.value[0].id.ToString()))
                    check = false;
            }
            return check;
        } 
	}
}
