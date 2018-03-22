#Region "Imports"
Imports System.Diagnostics
Imports AmosEngineLib.AmosEngine.TMatrixID
Imports System.Xml
Imports Amos
Imports System.Collections.Generic
Imports System.Linq
#End Region

<System.ComponentModel.Composition.Export(GetType(Amos.IPlugin))>
Public Class FitClass
    Implements Amos.IPlugin
    'This plugin was written by John Lim July 2016 for James Gaskin

    Public Function Name() As String Implements Amos.IPlugin.Name
        Return "Model Fit Measures"
    End Function

    Public Function Description() As String Implements Amos.IPlugin.Description
        Return "Puts important measures of model fit into a table on an html document. See statwiki.kolobkreations.com for more information."
    End Function


    Public Function Mainsub() As Integer Implements Amos.IPlugin.MainSub

        'Fits the specified model.
        pd.GetCheckBox("AnalysisPropertiesForm", "ModsCheck").Checked = True
        pd.GetCheckBox("AnalysisPropertiesForm", "ResidualMomCheck").Checked = True
        pd.AnalyzeCalculateEstimates()

        'Remove the old table files
        If (System.IO.File.Exists("ModelFit.html")) Then
            System.IO.File.Delete("ModelFit.html")
        End If

        'Start the amos debugger and create an object from the AmosEngine
        Dim debug As New AmosDebug.AmosDebug 'Set up the debugger
        Dim Sem As New AmosEngineLib.AmosEngine 'Access variables in the model such as df and Cmin
        Sem.NeedEstimates(SampleCorrelations) 'These two are for the SRMR
        Sem.NeedEstimates(ImpliedCorrelations)

        'Get CFI estimate from xpath expression. Modified version of the tutorial for xpath in the Amos Content helper updated for VB.NET from VB 6
        Dim baseline As XmlElement = GetXML("body/div/div[@ntype='modelfit']/div[@nodecaption='Baseline Comparisons']/table/tbody/tr[position() = 1]/td[position() = 6]")
        Dim CFI As Double = baseline.InnerText
        Dim tableSRC As XmlElement = GetXML("body/div/div[@ntype='models']/div[@ntype='model'][position() = 1]/div[@ntype='group'][position() = 1]/div[@ntype='estimates']/div[@ntype='matrices']/div[@ntype='ppml'][position() = 2]/table/tbody")
        Dim headSRC As XmlElement = GetXML("body/div/div[@ntype='models']/div[@ntype='model'][position() = 1]/div[@ntype='group'][position() = 1]/div[@ntype='estimates']/div[@ntype='matrices']/div[@ntype='ppml'][position() = 2]/table/thead")
        Dim tableRW As XmlElement = GetXML("body/div/div[@ntype='models']/div[@ntype='model'][position() = 1]/div[@ntype='group'][position() = 1]/div[@ntype='estimates']/div[@ntype='scalars']/div[@nodecaption='Regression Weights:']/table/tbody")

        'Count the observed variables in the model. Used to size arrays and loops
        Dim iObserved As Integer
        For Each a As PDElement In pd.PDElements
            If a.IsObservedVariable Then 'Checks if the variable is observed
                iObserved += 1 'Will return the number of variables in the model.
            End If
        Next

        'The following section checks if there are at least two unobserved variables connected to the latent variable
        'If there is less than three, the program will not recommend removing those latent variables.
        Dim listLatent As New List(Of String)()
        Dim iCount5 As Integer = 0
        For Each f As PDElement In pd.PDElements
            If f.IsLatentVariable Then
                For d = 1 To iObserved
                    If MatrixName(tableRW, d, 2) = f.NameOrCaption Then
                        iCount5 += 1
                    End If
                Next
                If iCount5 < 3 Then
                    listLatent.Add(f.NameOrCaption)
                End If
                iCount5 = 0
            End If
        Next

        'The list of latent variables with too few observed variables.
        Dim listFew As New List(Of String)()
        For Each latent As String In listLatent
            For d = 1 To iObserved
                If MatrixName(tableRW, d, 2) = latent Then
                    listFew.Add(MatrixName(tableRW, d, 0))
                End If
            Next
        Next

        'These counters are used to process the standardized residual covariances table
        Dim iCount As Integer = 0
        Dim iCount2 As Integer = iObserved
        Dim iCount3 As Integer = 0
        Dim iCount4 As Integer = 1
        Dim dSum As Double 'Stores the sum of values in the SRC
        Dim listValues As New List(Of varSummed) 'A list of objects that will hold a string and value
        For w = 0 To iObserved - 1 'For the number of observed variables
            Dim varName As String = MatrixName(headSRC, 1, (w + 1))
            For b = 1 To iCount2 'Add the column
                dSum = dSum + Math.Abs(MatrixElement(tableSRC, (b + iCount3), (w + 1)))
            Next
            For c = 1 To iCount4 'Add the row
                dSum = dSum + Math.Abs(MatrixElement(tableSRC, (w + 1), c))
            Next
            iCount2 -= 1
            iCount3 += 1
            iCount4 += 1
            Dim oValues As New varSummed(varName, dSum) 'Assign the name of the variable and the summed value to an object
            If Not listFew.Contains(varName) Then
                listValues.Add(oValues) 'Add object to list
            End If
            dSum = 0
        Next
        listValues = listValues.OrderBy(Function(x) x.Total).ToList() 'Sort the list of values

        Dim bConstraint As Boolean = False
        For d = 1 To iObserved
            If MatrixName(tableRW, d, 0) = listValues.First.Name And MatrixElement(tableRW, d, 3) = 1 Then
                bConstraint = True
            End If
        Next

        'Specify and fit the object to the model
        Amos.pd.SpecifyModel(Sem)
        Sem.FitModel()

        'Calculate SRMR
        Dim N As Integer
        Dim i As Integer
        Dim j As Integer
        Dim DTemp As Double
        Dim Sample(,) As Double
        Dim Implied(,) As Double
        Sem.GetEstimates(SampleCorrelations, Sample)
        Sem.GetEstimates(ImpliedCorrelations, Implied)
        N = UBound(Sample, 1) + 1
        DTemp = 0
        For i = 1 To N - 1
            For j = 0 To i - 1
                DTemp = DTemp + (Sample(i, j) - Implied(i, j)) ^ 2
            Next
        Next
        DTemp = System.Math.Sqrt(DTemp / (N * (N - 1) / 2))

        Dim CD As Double = Sem.Cmin / Sem.Df

        'Conditionals for interpretation column
        Dim sCD As String = ""
        Dim sCFI As String = ""
        Dim sSRMR As String = ""
        Dim sRMSEA As String = ""
        Dim sPclose As String = ""
        Dim iBad As Integer = 0
        Dim iGood As Integer = 0
        If CD > 5 Then
            sCD = "Terrible"
            iBad += 1
        ElseIf CD > 3 Then
            sCD = "Acceptable"
            iGood += 1
        Else
            sCD = "Excellent"
        End If
        If CFI > 0.95 Then
            sCFI = "Excellent"
        ElseIf CFI >= 0.9 Then
            sCFI = "Acceptable"
            iGood += 1
        Else
            sCFI = "Need More DF"
            iBad += 1
        End If
        If DTemp < 0.08 Then
            sSRMR = "Excellent"
        ElseIf DTemp <= 0.1 Then
            sSRMR = "Acceptable"
            iGood += 1
        Else
            sSRMR = "Terrible"
            iBad += 1
        End If
        If Sem.Rmsea < 0.06 Then
            sRMSEA = "Excellent"
        ElseIf Sem.Rmsea <= 0.08 Then
            sRMSEA = "Acceptable"
            iGood += 1
        Else
            sRMSEA = "Terrible"
            iBad += 1
        End If
        If Sem.Pclose > 0.05 Then
            sPclose = "Excellent"
        ElseIf Sem.Pclose > 0.01 Then
            sPclose = "Acceptable"
            iGood += 1
        Else
            sPclose = "Terrible"
            iBad += 1
        End If

        'Set up the listener To output the debugs
        Dim resultWriter As New TextWriterTraceListener("ModelFit.html")
        Trace.Listeners.Add(resultWriter)

        'Write the beginning Of the document
        debug.PrintX("<html><body><h1>Model Fit Measures</h1><hr/>")

        'Populate model fit measures in data table
        debug.PrintX("<table><tr><th>Measure</th><th>Estimate</th><th>Threshold</th><th>Interpretation</th></tr>")
        debug.PrintX("<tr><td>CMIN</td><td>")
        debug.PrintX(Sem.Cmin.ToString("#0.000"))
        debug.PrintX("</td><td>--</td><td>--</td></tr>")
        debug.PrintX("<tr><td>DF</td><td>")
        debug.PrintX(Sem.Df)
        debug.PrintX("</td><td>--</td><td>--</td></tr>")
        debug.PrintX("<tr><td>CMIN/DF</td><td>")
        debug.PrintX(CD.ToString("#0.000"))
        debug.PrintX("</td><td>Between 1 and 3</td><td>")
        debug.PrintX(sCD)
        debug.PrintX("</td></tr><tr><td>CFI</td><td>")
        debug.PrintX(CFI.ToString("#0.000"))
        debug.PrintX("</td><td>>0.95</td><td>")
        debug.PrintX(sCFI)
        debug.PrintX("</td></tr><tr><td>SRMR</td><td>")
        debug.PrintX(DTemp.ToString("#0.000"))
        debug.PrintX("</td><td><0.08</td><td>")
        debug.PrintX(sSRMR)
        debug.PrintX("</td></tr><tr><td>RMSEA</td><td>")
        debug.PrintX(Sem.Rmsea.ToString("#0.000"))
        debug.PrintX("</td><td><0.06</td><td>")
        debug.PrintX(sRMSEA)
        debug.PrintX("</td></tr><tr><td>PClose</td><td>")
        debug.PrintX(Sem.Pclose.ToString("#0.000"))
        debug.PrintX("</td><td>>0.05</td><td>")
        debug.PrintX(sPclose)
        debug.PrintX("</td></tr></table><br>")

        If iGood = 0 And iBad = 0 Then
            debug.PrintX("Congratulations, your model fit is excellent!")
        ElseIf iGood > 0 And iBad = 0 Then
            debug.PrintX("Congratulations, your model fit is acceptable.")
        Else
            debug.PrintX("Your model fit could improve. Based on the standardized residual covariances, we recommend removing " + listValues.First.Name + ".")
        End If

        If bConstraint = True Then
            debug.PrintX("<br>This indicator has a path constraint. You will need to change the constraint after removing " + listValues.First.Name + ".")
        End If

        'Write reference table and credits
        debug.PrintX("<hr/><h3> Cutoff Criteria*</h3><table><tr><th>Measure</th><th>Terrible</th><th>Acceptable</th><th>Excellent</th></tr>")
        debug.PrintX("<tr><td>CMIN/DF</td><td>> 5</td><td>> 3</td><td>> 1</td></tr>")
        debug.PrintX("</td></tr><tr><td>CFI</td><td><0.90</td><td><0.95</td><td>>0.95</td></tr>")
        debug.PrintX("</td></tr><tr><td>SRMR</td><td>>0.10</td><td>>0.08</td><td><0.08</td></tr>")
        debug.PrintX("</td></tr><tr><td>RMSEA</td><td>>0.08</td><td>>0.06</td><td><0.06</td></tr>")
        debug.PrintX("</td></tr><tr><td>PClose</td><td><0.01</td><td><0.05</td><td>>0.05</td></tr></table>")
        debug.PrintX("<p>*Note: Hu and Bentler (1999, ""Cutoff Criteria for Fit Indexes in Covariance Structure Analysis: Conventional Criteria Versus New Alternatives"") recommend combinations of measures. Personally, I prefer a combination of CFI>0.95 and SRMR<0.08. To further solidify evidence, add the RMSEA<0.06.</p>")
        debug.PrintX("<p>**If you would like to cite this tool directly, please use the following:")
        debug.PrintX("Gaskin, J. & Lim, J. (2016), ""Model Fit Measures"", AMOS Plugin. <a href=\""http://statwiki.kolobkreations.com"">Gaskination's StatWiki</a>.</p>")

        'Write Style And close
        debug.PrintX("<style>h1{margin-left:60px;}table{border:1px solid black;border-collapse:collapse;}td{border:1px solid black;text-align:center;padding:5px;}th{text-weight:bold;padding:10px;border: 1px solid black;}</style>")
        debug.PrintX("</body></html>")

        'Take down our debugging, release file, open html
        Trace.Flush()
        Trace.Listeners.Remove(resultWriter)
        resultWriter.Close()
        resultWriter.Dispose()
        Sem.Dispose()
        Process.Start("ModelFit.html")

    End Function

#Region "Helper Functions"

    'Use an output table path to get the xml version of the table.
    Public Function GetXML(path As String) As XmlElement

        'Gets the xpath expression for an output table.
        Dim doc As Xml.XmlDocument = New Xml.XmlDocument()
        doc.Load(Amos.pd.ProjectName & ".AmosOutput")
        Dim nsmgr As XmlNamespaceManager = New XmlNamespaceManager(doc.NameTable)
        Dim eRoot As Xml.XmlElement = doc.DocumentElement

        Return eRoot.SelectSingleNode(path, nsmgr)

    End Function

    'Get a string element from an xml table.
    Function MatrixName(eTableBody As XmlElement, row As Long, column As Long) As String

        Dim e As XmlElement

        Try
            e = eTableBody.ChildNodes(row - 1).ChildNodes(column) 'This means that the rows are not 0 based.
            MatrixName = e.InnerText
        Catch ex As Exception
            MatrixName = ""
        End Try

    End Function

    'Get a number from an xml table
    Function MatrixElement(eTableBody As XmlElement, row As Long, column As Long) As Double

        Dim e As XmlElement

        Try
            e = eTableBody.ChildNodes(row - 1).ChildNodes(column) 'This means that the rows are not 0 based.
            MatrixElement = CDbl(e.GetAttribute("x"))
        Catch ex As Exception
            MatrixElement = 0
        End Try

    End Function

#End Region

End Class

'Used for objects with a string and a value.
Public Class varSummed
    Public Name As String
    Public Total As Double

    Public Sub New(ByVal sName As String, ByVal dTotal As Double)
        'constructor
        Name = sName
        Total = dTotal
        'storing the values in constructor
    End Sub

End Class