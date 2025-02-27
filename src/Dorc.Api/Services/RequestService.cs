﻿using System.Security.Claims;
using Dorc.Api.Interfaces;
using Dorc.Api.Model;
using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;

namespace Dorc.Api.Services
{
    public class RequestService : IRequestService
    {
        private readonly IDeployableBuildFactory _deployableBuildFactory;
        private readonly ILog _log;
        private readonly IProjectsPersistentSource _projectsPersistentSource;

        public RequestService(ILog log, IDeployableBuildFactory deployableBuildFactory,
            IProjectsPersistentSource projectsPersistentSource)
        {
            _projectsPersistentSource = projectsPersistentSource;
            _log = log;
            _deployableBuildFactory = deployableBuildFactory;
        }

        public RequestStatusDto CreateRequest(RequestDto request, ClaimsPrincipal user)
        {
            CheckRequest(ref request);
            var build = _deployableBuildFactory.CreateInstance(request);
            if (build == null)
            {
                _log.Error($"Wrong build type: {request}");
                throw new WrongBuildTypeException($"Wrong build type. BuildUrl should start from 'http' or 'file' but got {request.BuildUrl}");
            }

            if (request.BuildNum != null && request.BuildNum.Contains(" [PINNED]"))
                request.BuildNum = request.BuildNum.Replace(" [PINNED]", "");

            if (build.IsValid(new BuildDetails(request)))
                return build.Process(request, user);

            _log.Error(new { Error = $"Build validation failed. {build.ValidationResult}" });
            throw new WrongBuildTypeException($"Build validation failed. {build.ValidationResult}");
        }

        public void CheckRequest(ref RequestDto request)
        {
            if (!string.IsNullOrEmpty(request.BuildUrl)) return;
            var projectName = request.Project;
            var project = _projectsPersistentSource.GetProject(projectName);
            if (project == null)
            {
                var msg = $"Unable to locate a project with the name '{projectName}'";
                _log.Error(msg);
                throw new Exception(msg);
            }
            request.BuildUrl = project.ArtefactsUrl.Split(';')[0];
        }
    }
}