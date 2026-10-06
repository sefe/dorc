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

	<xsl:key name="MonitorExeToRemove"
			 match="wix:Component[substring(wix:File/@Source, string-length(wix:File/@Source) - string-length('.Monitor.exe') +1)='.Monitor.exe']"
			 use="@Id"/>

	<!-- Prod Monitor -->

	<xsl:template match="wix:File[@Source='$(var.DorcMonitorDir)\appsettings.json']/@Id">
		<xsl:attribute name="{name()}">
			<xsl:value-of select="'ProdDeployMonitorExeConfig'" />
		</xsl:attribute>
	</xsl:template>

	<!-- Prod Net Framework Runner -->

	<xsl:template match="wix:File[@Source='$(var.DorcNetFrameworkRunnerDir)\appsettings.json']/@Id">
		<xsl:attribute name="{name()}">
			<xsl:value-of select="'ProdDeployRunnerExeConfig'" />
		</xsl:attribute>
	</xsl:template>

	<xsl:template match="wix:File[@Source='$(var.DorcNetFrameworkRunnerDir)\loggerSettings.json']/@Id">
		<xsl:attribute name="{name()}">
			<xsl:value-of select="'ProdDeployRunnerExeLoggingConfig'" />
		</xsl:attribute>
	</xsl:template>	

	<xsl:template match="wix:File[@Source='$(var.DorcNetFrameworkRunnerDir)\Dorc.NetFramework.Runner.exe']/@Id">
		<xsl:attribute name="{name()}">
			<xsl:value-of select="'ProdDeployRunnerExe'" />
		</xsl:attribute>
	</xsl:template>

	<!-- Prod Net Core Runner -->

	<xsl:template match="wix:File[@Source='$(var.DorcRunnerDir)\appsettings.json']/@Id">
		<xsl:attribute name="{name()}">
			<xsl:value-of select="'ProdDorcRunnerExeConfig'" />
		</xsl:attribute>
	</xsl:template>

	<xsl:template match="wix:File[@Source='$(var.DorcRunnerDir)\Dorc.Runner.exe']/@Id">
		<xsl:attribute name="{name()}">
			<xsl:value-of select="'ProdDorcRunnerExe'" />
		</xsl:attribute>
	</xsl:template>

	<xsl:template match="wix:File[@Source='$(var.DorcRunnerDir)\loggerSettings.json']/@Id">
		<xsl:attribute name="{name()}">
			<xsl:value-of select="'ProdDorcRunnerExeLoggingConfig'" />
		</xsl:attribute>
	</xsl:template>

	<!-- Prod Terraform Runner -->

	<xsl:template match="wix:File[@Source='$(var.DorcTerraformRunnerDir)\appsettings.json']/@Id">
		<xsl:attribute name="{name()}">
			<xsl:value-of select="'ProdTerraformRunnerExeConfig'" />
		</xsl:attribute>
	</xsl:template>

	<xsl:template match="wix:File[@Source='$(var.DorcTerraformRunnerDir)\loggerSettings.json']/@Id">
		<xsl:attribute name="{name()}">
			<xsl:value-of select="'ProdTerraformRunnerExeLoggingConfig'" />
		</xsl:attribute>
	</xsl:template>

	<xsl:template match="wix:File[@Source='$(var.DorcTerraformRunnerDir)\Dorc.TerraformRunner.exe']/@Id">
		<xsl:attribute name="{name()}">
			<xsl:value-of select="'ProdTerraformRunnerExe'" />
		</xsl:attribute>
	</xsl:template>
	
	<xsl:template match="*[self::wix:Component or self::wix:ComponentRef]
                        [key('MonitorExeToRemove', @Id)]" />

	<xsl:template match="*[self::wix:Component or self::wix:ComponentRef]
                        [key('XmlToRemove', @Id)]" />

	<xsl:template match="*[self::wix:Component or self::wix:ComponentRef]
                        [key('PdbToRemove', @Id)]" />

</xsl:stylesheet>