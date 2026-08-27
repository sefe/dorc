<?xml version="1.0" encoding="utf-8"?>

<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:msxsl="urn:schemas-microsoft-com:xslt"
                exclude-result-prefixes="msxsl"
                xmlns:wix="http://wixtoolset.org/schemas/v4/wxs">

	<xsl:output method="xml" indent="yes" />

	<xsl:template match="@* | node()">
		<xsl:copy>
			<xsl:apply-templates select="@* | node()" />
		</xsl:copy>
	</xsl:template>

	<xsl:key name="XmlToRemove"
			 match="wix:Component[contains(wix:File/@Source, '.xml')]"
			 use="@Id" />

	<xsl:key name="PdbToRemove"
			 match="wix:Component[contains(wix:File/@Source, '.pdb')]"
			 use="@Id" />

	<!-- The service component in ApiWindowsWorker.wxs installs the exe itself so the
	     ServiceInstall can key on it; drop the harvested copy. -->
	<xsl:key name="WorkerExeToRemove"
			 match="wix:Component[substring(wix:File/@Source, string-length(wix:File/@Source) - string-length('.WindowsWorker.exe') +1)='.WindowsWorker.exe']"
			 use="@Id"/>

	<xsl:template match="wix:File[@Source='$(var.DorcApiWindowsWorkerDir)\appsettings.json']/@Id">
		<xsl:attribute name="{name()}">
			<xsl:value-of select="'ApiWindowsWorkerExeConfig'" />
		</xsl:attribute>
	</xsl:template>

	<xsl:template match="wix:Component[key('XmlToRemove', @Id)]" />
	<xsl:template match="wix:ComponentRef[key('XmlToRemove', @Id)]" />

	<xsl:template match="wix:Component[key('PdbToRemove', @Id)]" />
	<xsl:template match="wix:ComponentRef[key('PdbToRemove', @Id)]" />

	<xsl:template match="wix:Component[key('WorkerExeToRemove', @Id)]" />
	<xsl:template match="wix:ComponentRef[key('WorkerExeToRemove', @Id)]" />
</xsl:stylesheet>
