// Copyright (c) Microsoft Open Technologies, Inc. All Rights Reserved.
// Licensed under the MIT License. See LICENSE in the source repository root for license information.﻿

using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Microsoft.VisualStudio.TextTemplating;
using Vipr.T4TemplateWriter.Extensions;
using Vipr.T4TemplateWriter.Output;
using Vipr.T4TemplateWriter;
using Vipr.Core;
using Vipr.Core.CodeModel;

namespace Vipr.T4TemplateWriter.TemplateProcessor {

    public enum FileType {
        ComplexType,
        EntityType,
        EnumType,
        EntityFetcher,
        EntityRequestBase, //added
        EntityRequest, //added
        EntityRequestBuilderBase, //added
        EntityRequestBuilder, //added
        EntityCollection, //added
        Stream, //added
        Method, //added
        EntityOperations,
        EntityCollectionFetcher,
        EntityCollectionOperations,
        EntityClient,
        Other,
        Unknown
    }

    public class TemplateProcessor : ITemplateProcessor {

        private static CustomT4Host _host;
        protected static CustomT4Host Host(TemplateFileInfo templateInfo, String templatesDirectory, OdcmObject odcmObject, OdcmModel odcmModel) {
            if (_host == null) {
                _host = new CustomT4Host(templateInfo, templatesDirectory, odcmObject, odcmModel);
            } else {
                _host.Reset(templateInfo, templatesDirectory, odcmObject, odcmModel);
            }

            return _host;      
        }

        protected Dictionary<FileType, Func<TemplateFileInfo, IEnumerable<TextFile>>> _subProcessors;
        protected Dictionary<FileType, Func<TemplateFileInfo, IEnumerable<TextFile>>> SubProcessors {
            get { 
                if (null == _subProcessors) {
                    InitializeSubprocessors();
                }
                return _subProcessors;
            }
        }

        protected void InitializeSubprocessors() {
            _subProcessors = new Dictionary<FileType, Func<TemplateFileInfo, IEnumerable<TextFile>>>() {             
                // Models
                {FileType.EntityType,                   ProcessEntityTypes},
                {FileType.ComplexType,                  ProcessComplexTypes},
                {FileType.EnumType,                     ProcessEnumTypes},
               
                // Fetchers
                {FileType.EntityCollectionFetcher,      ProcessEntityTypes},
                {FileType.EntityCollectionOperations,   ProcessEntityTypes},
                {FileType.EntityFetcher,                ProcessEntityTypes},
                {FileType.EntityRequest,                ProcessEntityTypes}, //added
                {FileType.EntityRequestBuilder,         ProcessEntityTypes}, //added
                {FileType.EntityRequestBase,            ProcessEntityTypes}, //added
                {FileType.EntityRequestBuilderBase,     ProcessEntityTypes}, //added
                {FileType.EntityCollection,             ProcessCollectionProperties}, //added
                {FileType.Method,                       ProcessMethodTypes},         
                {FileType.Stream,                       ProcessStreamProperties},     
                {FileType.EntityOperations,             ProcessEntityTypes}, //REMEMBER

                // EntityContainer
                {FileType.EntityClient,                 ProcessEntityContainerType},

                // Other
                {FileType.Other,                        ProcessTemplate},
            };
        }

        protected OdcmModel CurrentModel { get; set; }
        protected Microsoft.VisualStudio.TextTemplating.Engine T4Engine { get; set; }
        protected IPathWriter PathWriter { get; set; }
        protected String TemplatesDirectory { get; set; }

        public TemplateProcessor(IPathWriter pathWriter, OdcmModel odcmModel, String templatesDirectory) {
            this.T4Engine = new Microsoft.VisualStudio.TextTemplating.Engine();
            this.CurrentModel = odcmModel;
            this.PathWriter = pathWriter;
            this.PathWriter.Model = odcmModel;
            this.TemplatesDirectory = templatesDirectory;
        }

        public IEnumerable<TextFile> Process(TemplateFileInfo templateInfo) {
            FileType fileType;
            Func<TemplateFileInfo, IEnumerable<TextFile>> subProcessor;

            Boolean valid = Enum.TryParse(templateInfo.TemplateBaseName, true, out fileType);

            if (valid) {
                SubProcessors.TryGetValue(fileType, out subProcessor);
            } else {
                SubProcessors.TryGetValue(FileType.Other, out subProcessor);
            }
            return subProcessor(templateInfo);
        }


        protected virtual IEnumerable<TextFile> ProcessEntityContainerType(TemplateFileInfo templateInfo) {
            var container = this.CurrentModel.EntityContainer;
            yield return ProcessTemplate(templateInfo, container);
        }

        protected virtual IEnumerable<TextFile> ProcessEnumTypes(TemplateFileInfo templateInfo) {
            var enumTypes = CurrentModel.GetEnumTypes();
            foreach (OdcmObject enumType in enumTypes) {
                yield return ProcessTemplate(templateInfo, enumType);
            }
        }

        protected virtual IEnumerable<TextFile> ProcessComplexTypes(TemplateFileInfo templateInfo) {
            var complexTypes = CurrentModel.GetComplexTypes();
            foreach (OdcmObject complexType in complexTypes) {
                yield return ProcessTemplate(templateInfo, complexType);
            }
        }

        protected virtual IEnumerable<TextFile> ProcessEntityTypes(TemplateFileInfo templateInfo) {
            var types = CurrentModel.GetEntityTypes();
            var complexNavigableTypes = CurrentModel.GetComplexTypes().WhereIsNavigable();

            types = types.Union(complexNavigableTypes);

            foreach (OdcmObject entityType in types) {
                yield return ProcessTemplate(templateInfo, entityType);
            }
        }

        public IEnumerable<TextFile> ProcessCollectionProperties(TemplateFileInfo templateInfo)
        {
            var collections = CurrentModel.GetEntityTypes().
                SelectMany(et => et.Properties).
                Where(prop => prop.IsCollection && prop.Type != null && prop.Type.Name != "String");

            var containerEntitySets = CurrentModel.EntityContainer.Properties;
            containerEntitySets.ForEach(set => set.IsCollection = true);

            collections = collections.Union(containerEntitySets);

            foreach (OdcmObject collection in collections)
            {
                yield return ProcessTemplate(templateInfo, collection);
            }
        }

        public IEnumerable<TextFile> ProcessMethodTypes(TemplateFileInfo templateInfo)
        {
            var methods = CurrentModel.GetEntityTypes()
                .SelectMany(et => et.Methods);

            foreach (OdcmMethod method in methods)
            {
                string templateName = templateInfo.TemplateBaseName.Replace(
                    "Method",
                    method.Class.Name.ToUpperFirstChar() +
                        method.Name.Substring(method.Name.IndexOf('.') + 1).ToUpperFirstChar());
                yield return ProcessTemplate(templateInfo, method, templateName);
            }
        }

        public IEnumerable<TextFile> ProcessStreamProperties(TemplateFileInfo templateInfo)
        {
            var properties = CurrentModel.GetEntityTypes()
                .SelectMany(et => et.Properties)
                .Where(prop => prop.IsStream);

            var complexStreamProperties = CurrentModel.GetComplexTypes()
                .SelectMany(ct => ct.Properties)
                .Where(prop => prop.IsStream);

            properties = properties.Union(complexStreamProperties);

            foreach (OdcmProperty stream in properties)
            {
                string templateName = templateInfo.TemplateBaseName.Replace(
                    "Stream",
                    stream.Class.Name.ToUpperFirstChar() + stream.Name.ToUpperFirstChar());
                yield return ProcessTemplate(templateInfo, stream, templateName);
            }

        }

        protected IEnumerable<TextFile> ProcessTemplate(TemplateFileInfo templateInfo) {
            yield return this.ProcessTemplate(templateInfo, null);
        }

        protected TextFile ProcessTemplate(TemplateFileInfo templateInfo, OdcmObject odcmObject, String fileName = null) {
            var host = TemplateProcessor.Host(templateInfo, this.TemplatesDirectory, odcmObject, this.CurrentModel);

            var templateContent = File.ReadAllText(host.TemplateFile);

            var output = this.T4Engine.ProcessTemplate(templateContent, host);

            if (host.Errors != null && host.Errors.HasErrors) {
                var errors = this.LogErrors(host, templateInfo);
                throw new InvalidOperationException(errors);
            }

            if (fileName == null) {
                fileName = (odcmObject != null) ? odcmObject.Name.ToUpperFirstChar() : templateInfo.TemplateBaseName.ToUpperFirstChar();
            }

            var path = this.PathWriter.WritePath(templateInfo, fileName);

            return new TextFile(path, output);
        }

        public string LogErrors(CustomT4Host host, TemplateFileInfo templateInfo) {
            var sb = new StringBuilder();
            if (host.Errors == null || host.Errors.Count <= 0) return sb.ToString();

            foreach (CompilerError error in host.Errors) {
                sb.AppendLine("TemplateProcessor ERROR").
                    AppendFormat(@"Name:     {0}{1}", templateInfo.TemplateName, Environment.NewLine).
                    AppendFormat(@"Line:     {0}{1}", error.Line, Environment.NewLine).
                    AppendFormat(@"Details:  {0}{1}", error.ErrorText, Environment.NewLine).
                    AppendLine().
                    AppendLine();
            }
            return sb.ToString();
        }
    }
}
